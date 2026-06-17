using System.IO;
using System.Linq;
using System.Reflection;
using IziProxy;
using Xunit;

namespace IziProxy.Tests;

/// <summary>
/// Тесты для <see cref="EmbeddedScripts"/> (F-09 из FutureTest.md).
/// Покрывают: вшитые ресурсы MainInstall.sh / Deploy.sh / config.json
/// существуют, не пустые, валидный JSON с плейсхолдерами для sed,
/// exception при потере ресурса.
/// </summary>
public class EmbeddedScriptsTests
{
    [Fact]
    public void OpenMainInstall_ReturnsNonEmptyStream()
    {
        using var stream = EmbeddedScripts.OpenMainInstall();

        Assert.NotNull(stream);
        Assert.True(stream.Length > 0, "MainInstall.sh stream должен быть непустым");
    }

    [Fact]
    public void OpenDeploy_ReturnsNonEmptyStream()
    {
        using var stream = EmbeddedScripts.OpenDeploy();

        Assert.NotNull(stream);
        Assert.True(stream.Length > 0, "Deploy.sh stream должен быть непустым");
    }

    [Fact]
    public void ReadConfigJson_ReturnsNonEmptyContent()
    {
        // НЕ пытаемся парсить как JSON — config.json содержит
        // __PORT_1__/__SNI_1__ плейсхолдеры для sed-замены, которые
        // НЕ являются валидным JSON до подстановки. Deploy.sh делает
        // замену ДО того, как Xray парсит файл.
        string json = EmbeddedScripts.ReadConfigJson();

        Assert.False(string.IsNullOrWhiteSpace(json));
        Assert.True(json.Length > 100, "config.json должен быть существенно длиннее 100 байт");
        // Базовая JSON-структура (после замены плейсхолдеров) — корневые ключи
        Assert.Contains("\"inbounds\"", json);
        Assert.Contains("\"outbounds\"", json);
        Assert.Contains("\"routing\"", json);
    }

    [Fact]
    public void ReadConfigJson_ContainsExpectedPlaceholders()
    {
        // Deploy.sh делает sed-замены __PORT_1__, __SNI_1__, __UUID__, etc.
        // Все эти плейсхолдеры должны быть в исходном config.json.
        string json = EmbeddedScripts.ReadConfigJson();

        Assert.Contains("__UUID__", json);
        Assert.Contains("__PRIVATE_KEY__", json);
        Assert.Contains("__SHORT_ID__", json);
        Assert.Contains("__PORT_1__", json);
        Assert.Contains("__PORT_2__", json);
        Assert.Contains("__PORT_3__", json);
        Assert.Contains("__SNI_1__", json);
        Assert.Contains("__SNI_2__", json);
        Assert.Contains("__SNI_3__", json);
    }

    [Fact]
    public void ReadConfigJson_HasThreeInboundBlocks()
    {
        // IziProxy генерирует 3 inbound'а (порты 443, 8443, random)
        // для отказоустойчивости. Config.json должен содержать три
        // блока "tag": "inbound-N" с N=1,2,3.
        string json = EmbeddedScripts.ReadConfigJson();

        Assert.Contains("\"tag\": \"inbound-1\"", json);
        Assert.Contains("\"tag\": \"inbound-2\"", json);
        Assert.Contains("\"tag\": \"inbound-3\"", json);
        Assert.Contains("\"protocol\": \"vless\"", json);
        Assert.Contains("\"security\": \"reality\"", json);
    }

    [Fact]
    public void ReadConfigJson_HasRoutingRulesForDirectBypass()
    {
        // config.json содержит 43+ RU-домена для direct-bypass
        // (см. коммит 'адаптация интерфейса под мобильные устройства').
        // Проверяем что хотя бы ключевые домены на месте.
        string json = EmbeddedScripts.ReadConfigJson();

        Assert.Contains("yandex.ru", json);
        Assert.Contains("vk.com", json);
        Assert.Contains("gosuslugi.ru", json);
    }

    [Fact]
    public void ReadConfigJson_HasApiConfig_ForTrafficStats()
    {
        // Для работы XrayMonitor.GetStatus (Dashboard) config.json
        // должен включать api+stats. Без этого xray api statsquery
        // вернёт ошибку.
        string json = EmbeddedScripts.ReadConfigJson();

        Assert.Contains("\"api\"", json);
        Assert.Contains("127.0.0.1:10085", json);
        Assert.Contains("StatsService", json);
        // policy.system включает stats tracking для inbound traffic
        Assert.Contains("statsInboundUplink", json);
        Assert.Contains("statsInboundDownlink", json);
    }

    [Fact]
    public void OpenMainInstall_HasShebang_AndAptGetInstall()
    {
        // MainInstall.sh должен начинаться с shebang и вызывать apt-get
        // install — это первое что увидит VDS-скрипт.
        using var stream = EmbeddedScripts.OpenMainInstall();
        using var reader = new StreamReader(stream);
        string content = reader.ReadToEnd();

        Assert.StartsWith("#!/bin/bash", content);
        Assert.Contains("apt-get install", content);
    }

    [Fact]
    public void OpenDeploy_HasShebang_AndPortSelection()
    {
        using var stream = EmbeddedScripts.OpenDeploy();
        using var reader = new StreamReader(stream);
        string content = reader.ReadToEnd();

        Assert.StartsWith("#!/bin/bash", content);
        Assert.Contains("SELECTED_PORT_1=", content);
        Assert.Contains("SELECTED_PORT_2=", content);
        Assert.Contains("SELECTED_PORT_3=", content);
        Assert.Contains("SNI_SELECTED_1=", content);
        Assert.Contains("SNI_SELECTED_2=", content);
        Assert.Contains("SNI_SELECTED_3=", content);
    }

    [Fact]
    public void AllResourcesAreRegisteredAsEmbeddedResources()
    {
        // Если кто-то добавит новый файл в VDS_setup/ но забудет
        // <EmbeddedResource Include="..." /> в csproj — ресурс не
        // вшиется, и Open() упадёт в runtime. Этот тест ловит такие
        // регрессии на этапе CI.
        var asm = typeof(EmbeddedScripts).Assembly;
        string[] resourceNames = asm.GetManifestResourceNames();

        // EmbeddedScripts.Prefix = "IziProxy.Core.VDS_setup."
        string[] expected = new[]
        {
            "IziProxy.Core.VDS_setup.MainInstall.sh",
            "IziProxy.Core.VDS_setup.Deploy.sh",
            "IziProxy.Core.VDS_setup.config.json",
        };

        foreach (var exp in expected)
        {
            Assert.Contains(exp, resourceNames);
        }
    }

    [Fact]
    public void Open_NonExistentResource_ThrowsFileNotFoundException()
    {
        // Рефлексия вызывает private Open() с несуществующим ресурсом —
        // проверяем что exception осмысленный (содержит имя ресурса +
        // список доступных).
        var method = typeof(EmbeddedScripts).GetMethod("Open",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var ex = Assert.Throws<TargetInvocationException>(() =>
            method!.Invoke(null, new object[] { "NonExistent.sh" }));

        // Внутренний exception — FileNotFoundException с осмысленным message
        Assert.IsType<FileNotFoundException>(ex.InnerException);
        Assert.Contains("NonExistent.sh", ex.InnerException!.Message);
        Assert.Contains("Доступные ресурсы", ex.InnerException.Message);
    }
}
