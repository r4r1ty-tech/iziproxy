using System;
using System.Collections.Generic;
using Xunit;

namespace IziProxy.Tests;

/// <summary>
/// Smoke-тесты для <see cref="VlessLinkGenerator"/> (F-04 из FutureTest.md).
/// Покрывают: happy path (уже был), Unicode в SNI/имени, спецсимволы
/// в имени ссылки, разные длины Ports/Snis (IndexOutOfRangeException
/// ожидаемо для текущей реализации — это часть gap-анализа, не баг),
/// несколько инбоундов.
/// </summary>
public class VlessLinkGeneratorTests
{
    [Fact]
    public void GenerateRealityLink_BuildsExpectedLink()
    {
        var serverConfig = new ServerConfig { Host = "example.com" };
        var xrayParams = new XrayConfigParams
        {
            Uuid = "uuid-123",
            Password = "pub key",
            ShortId = "short-id",
            Ports = new List<string> { "8443" },
            Snis = new List<string> { "www.microsoft.com" }
        };

        List<string> links = VlessLinkGenerator.GenerateRealityLinks(serverConfig, xrayParams, "My Link");

        Assert.Single(links);
        string link = links[0];

        string query = string.Join("&", new[]
        {
            "type=xhttp",
            "security=reality",
            $"pbk={Uri.EscapeDataString(xrayParams.Password)}",
            "fp=chrome",
            $"sni={Uri.EscapeDataString("www.microsoft.com")}",
            $"sid={Uri.EscapeDataString(xrayParams.ShortId)}",
            $"path={Uri.EscapeDataString("/xh-query")}",
        });

        string expected = $"vless://{xrayParams.Uuid}@{serverConfig.Host}:8443?{query}#{Uri.EscapeDataString("My Link_1")}";

        Assert.Equal(expected, link);
    }

    [Fact]
    public void GenerateRealityLink_MultipleInbounds_GeneratesMultipleLinks()
    {
        // Deploy.sh создаёт 3 inbound'а (порты 443, 8443, random) — проверим
        // что GenerateRealityLinks возвращает 3 ссылки с правильными именами.
        var serverConfig = new ServerConfig { Host = "vds.example.com" };
        var xrayParams = new XrayConfigParams
        {
            Uuid = "uuid-xyz",
            Password = "publicKey",
            ShortId = "abcd1234",
            Ports = new List<string> { "443", "8443", "50001" },
            Snis = new List<string> { "cdn.example.com", "static.example.com", "speed.example.com" }
        };

        var links = VlessLinkGenerator.GenerateRealityLinks(serverConfig, xrayParams, "IziProxy_VDS");

        Assert.Equal(3, links.Count);
        Assert.Contains("vds.example.com:443", links[0]);
        Assert.Contains("sni=cdn.example.com", links[0]);
        Assert.Contains("IziProxy_VDS_1", links[0]);

        Assert.Contains("vds.example.com:8443", links[1]);
        Assert.Contains("sni=static.example.com", links[1]);
        Assert.Contains("IziProxy_VDS_2", links[1]);

        Assert.Contains("vds.example.com:50001", links[2]);
        Assert.Contains("sni=speed.example.com", links[2]);
        Assert.Contains("IziProxy_VDS_3", links[2]);
    }

    [Fact]
    public void GenerateRealityLink_UnicodeSni_IsEscaped()
    {
        // RFC 3986: URL-encode все non-ASCII символы. Кириллический домен
        // должен быть percent-encoded. На момент проверки ни один
        // существующий клиент (v2rayN, Nekobox, v2rayNG) не понимает IDN,
        // так что это скорее edge case, но ссылка должна быть валидным URL.
        var serverConfig = new ServerConfig { Host = "xn--example.com" };
        var xrayParams = new XrayConfigParams
        {
            Uuid = "uuid",
            Password = "pbk",
            ShortId = "sid",
            Ports = new List<string> { "443" },
            Snis = new List<string> { "пример.рф" }
        };

        var links = VlessLinkGenerator.GenerateRealityLinks(serverConfig, xrayParams);

        Assert.Single(links);
        // IDN-домен в SNI должен быть percent-encoded, не raw bytes.
        Assert.Contains("%D0%BF%D1%80%D0%B8%D0%BC%D0%B5%D1%80", links[0]);
        // Ссылка всё равно начинается с vless:// — это валидный URL.
        Assert.StartsWith("vless://uuid@", links[0]);
    }

    [Fact]
    public void GenerateRealityLink_SpecialCharsInName_AreEscaped()
    {
        // Имя ссылки (fragment) содержит спецсимволы. По RFC 3986 fragment
        // — это всё после '#', в нём '/' и '?' должны быть экранированы.
        // Текущая реализация вызывает Uri.EscapeDataString — это правильно
        // (escape data, не component), но проверяем явно.
        var serverConfig = new ServerConfig { Host = "vds.com" };
        var xrayParams = new XrayConfigParams
        {
            Uuid = "uuid",
            Password = "pbk",
            ShortId = "sid",
            Ports = new List<string> { "443" },
            Snis = new List<string> { "sni.com" }
        };

        var links = VlessLinkGenerator.GenerateRealityLinks(serverConfig, xrayParams, "My VDS #1 / work");

        Assert.Single(links);
        // Пробелы, #, / в имени ссылки должны быть percent-encoded
        Assert.Contains("#My%20VDS%20%231%20%2F%20work_1", links[0]);
    }

    [Fact]
    public void GenerateRealityLink_EmptySni_FallsBackToMicrosoftCom_NotYetException()
    {
        // ISS-09: silent fallback на www.microsoft.com. Это документированное
        // поведение текущей реализации, не баг. Тест ЗАКРЕПЛЯЕТ это поведение.
        // Когда ISS-09 будет пофикшен (поднимать exception), этот тест
        // обновится на `Assert.Throws<InvalidOperationException>`.
        var serverConfig = new ServerConfig { Host = "vds.com" };
        var xrayParams = new XrayConfigParams
        {
            Uuid = "uuid",
            Password = "pbk",
            ShortId = "sid",
            Ports = new List<string> { "443" },
            Snis = new List<string> { "" }
        };

        var links = VlessLinkGenerator.GenerateRealityLinks(serverConfig, xrayParams);

        Assert.Single(links);
        Assert.Contains("sni=www.microsoft.com", links[0]);
    }

    [Fact]
    public void GenerateRealityLink_WhitespaceSni_FallsBackToMicrosoftCom()
    {
        // IsNullOrWhiteSpace ловит и "", и "   " — обе silent fallback'ятся.
        var serverConfig = new ServerConfig { Host = "vds.com" };
        var xrayParams = new XrayConfigParams
        {
            Uuid = "uuid",
            Password = "pbk",
            ShortId = "sid",
            Ports = new List<string> { "443" },
            Snis = new List<string> { "   " }
        };

        var links = VlessLinkGenerator.GenerateRealityLinks(serverConfig, xrayParams);

        Assert.Single(links);
        Assert.Contains("sni=www.microsoft.com", links[0]);
    }

    [Fact]
    public void GenerateRealityLink_PortsFewerThanSnis_GeneratesFewerLinks()
    {
        // Цикл идёт до Ports.Count, не до Snis.Count. Если Snis длиннее
        // чем Ports — лишние Snis игнорируются, возвращается только
        // Ports.Count ссылок. Это gap-анализ, не баг: текущая реализация
        // НЕ валидирует, что Ports.Count == Snis.Count.
        var serverConfig = new ServerConfig { Host = "vds.com" };
        var xrayParams = new XrayConfigParams
        {
            Uuid = "uuid",
            Password = "pbk",
            ShortId = "sid",
            Ports = new List<string> { "443" },             // 1 port
            Snis = new List<string> { "sni1.com", "sni2.com" } // 2 snis (sni2 ignored)
        };

        var links = VlessLinkGenerator.GenerateRealityLinks(serverConfig, xrayParams);

        Assert.Single(links);
        Assert.Contains("sni=sni1.com", links[0]);
    }

    [Fact]
    public void GenerateRealityLink_PortsMoreThanSnis_ThrowsOnSecondIteration()
    {
        // Обратный случай: Ports.Count > Snis.Count. Цикл идёт до Ports.Count
        // и на i >= Snis.Count падает с IndexOutOfRangeException на Snis[i].
        // Это наша вина — Deploy.sh гарантирует что они одинаковой длины,
        // но UI должен это проверять. Документируем реальное поведение.
        var serverConfig = new ServerConfig { Host = "vds.com" };
        var xrayParams = new XrayConfigParams
        {
            Uuid = "uuid",
            Password = "pbk",
            ShortId = "sid",
            Ports = new List<string> { "443", "8443" },      // 2 ports
            Snis = new List<string> { "sni1.com" }           // 1 sni
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VlessLinkGenerator.GenerateRealityLinks(serverConfig, xrayParams));
    }

    [Fact]
    public void GenerateRealityLink_SpecialCharsInPublicKey_AreEscaped()
    {
        // x25519 PublicKey — это base64, но base64 от x25519 включает
        // '+' и '/' которые требуют URL-escape. Uri.EscapeDataString
        // кодирует их в %2B и %2F.
        var serverConfig = new ServerConfig { Host = "vds.com" };
        var pbk = "abc+def/ghi="; // base64 с + и /
        var xrayParams = new XrayConfigParams
        {
            Uuid = "uuid",
            Password = pbk,
            ShortId = "sid",
            Ports = new List<string> { "443" },
            Snis = new List<string> { "sni.com" }
        };

        var links = VlessLinkGenerator.GenerateRealityLinks(serverConfig, xrayParams);

        Assert.Single(links);
        // pbk в URL должен быть percent-encoded
        Assert.Contains("pbk=abc%2Bdef%2Fghi%3D", links[0]);
        // И НЕ должен содержать raw + / =
        Assert.DoesNotContain("pbk=abc+def", links[0]);
    }
}
