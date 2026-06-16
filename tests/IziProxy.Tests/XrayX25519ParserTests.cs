using System;
using Xunit;

namespace IziProxy.Tests;

/// <summary>
/// Тесты для <see cref="XrayConfigParams.ParseX25519Output"/> (F-05 из FutureTest.md).
/// Покрывают три формата вывода <c>xray x25519</c>: старый основной,
/// альтернативный старый, и новый (xray v25.3.6+) с Hash32.
/// </summary>
public class XrayX25519ParserTests
{
    [Fact]
    public void Parse_OldFormat_WithPasswordPublicKey_ReturnsPair()
    {
        // Старый формат xray-core: "PrivateKey:" + "Password (PublicKey):"
        const string output = """
            PrivateKey: 6QWGd-eRHeWT3NGgnazO0xAV1mUnH1HKsm4N8ny8Ny0
            Password (PublicKey): mYMNs1GFI_y0cBx6_mtSn8vyjQqU8F2cVx1J6dsjT28
            """;

        var (priv, pub) = XrayConfigParams.ParseX25519Output(output);

        Assert.Equal("6QWGd-eRHeWT3NGgnazO0xAV1mUnH1HKsm4N8ny8Ny0", priv);
        Assert.Equal("mYMNs1GFI_y0cBx6_mtSn8vyjQqU8F2cVx1J6dsjT28", pub);
    }

    [Fact]
    public void Parse_OldFormat_WithPrivateKeyPublicKey_ReturnsPair()
    {
        // Альтернативный старый формат (встречается в выводах старых xray):
        // "Private key:" + "Public key:" (с пробелами, без скобок).
        const string output = """
            Private key: 6QWGd-eRHeWT3NGgnazO0xAV1mUnH1HKsm4N8ny8Ny0
            Public key: mYMNs1GFI_y0cBx6_mtSn8vyjQqU8F2cVx1J6dsjT28
            """;

        var (priv, pub) = XrayConfigParams.ParseX25519Output(output);

        Assert.Equal("6QWGd-eRHeWT3NGgnazO0xAV1mUnH1HKsm4N8ny8Ny0", priv);
        Assert.Equal("mYMNs1GFI_y0cBx6_mtSn8vyjQqU8F2cVx1J6dsjT28", pub);
    }

    [Fact]
    public void Parse_NewFormat_WithPasswordAndHash32_ReturnsPair()
    {
        // НОВЫЙ формат xray v25.3.6+ (https://github.com/XTLS/Xray-core/discussions/5159):
        // "PrivateKey:" + "Password:" + "Hash32:". Парсим только первые два —
        // Hash32 не нужен для конфигурации VLESS Reality.
        // Раньше в IziProxy парсинг "Password:" НЕ поддерживался — этот тест
        // закрывает gap (PR #1972 в Marzban, декабрь 2025).
        const string output = """
            PrivateKey: 6QWGd-eRHeWT3NGgnazO0xAV1mUnH1HKsm4N8ny8Ny0
            Password: mYMNs1GFI_y0cBx6_mtSn8vyjQqU8F2cVx1J6dsjT28
            Hash32: 4d6f6beb5a9070e4e8d1c0e0d0e0d0e0d0e0d0e0d0e0d0e0d0e0d0e0d0e0d0e0
            """;

        var (priv, pub) = XrayConfigParams.ParseX25519Output(output);

        Assert.Equal("6QWGd-eRHeWT3NGgnazO0xAV1mUnH1HKsm4N8ny8Ny0", priv);
        Assert.Equal("mYMNs1GFI_y0cBx6_mtSn8vyjQqU8F2cVx1J6dsjT28", pub);
    }

    [Fact]
    public void Parse_MixedFormatOrder_PrefersExplicitPublicKey()
    {
        // Если в одном выводе встретились оба варианта PublicKey ("Password (PublicKey):"
        // и "Public key:" в другом месте) — берётся последний найденный.
        // Это документирует текущее поведение "последний wins" — если в
        // будущем xray начнёт выводить оба, нужно будет задуматься.
        const string output = """
            PrivateKey: abc
            Password (PublicKey): old
            Public key: new
            """;

        var (_, pub) = XrayConfigParams.ParseX25519Output(output);

        Assert.Equal("new", pub);
    }

    [Fact]
    public void Parse_EmptyOrWhitespace_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => XrayConfigParams.ParseX25519Output(""));
        Assert.Throws<ArgumentException>(() => XrayConfigParams.ParseX25519Output("   \n\n   "));
    }

    [Fact]
    public void Parse_NullOrMissing_Throws()
    {
        // string.IsNullOrWhiteSpace(null) возвращает true, поэтому null
        // ловится первой проверкой и кидает ArgumentException (а не
        // ArgumentNullException). Это документированное поведение —
        // string.IsNullOrWhiteSpace не различает null и пустую строку.
        Assert.Throws<ArgumentException>(() => XrayConfigParams.ParseX25519Output(null!));
    }

    [Fact]
    public void Parse_NoKeysPresent_ThrowsFormatException()
    {
        // xray вернул что-то странное, ни PrivateKey ни PublicKey нет.
        // Должны получить FormatException с указанием ожидаемых префиксов.
        const string output = "Some random xray error message\nNothing useful here";

        var ex = Assert.Throws<FormatException>(() => XrayConfigParams.ParseX25519Output(output));

        // Сообщение должно содержать подсказку какие префиксы мы ждём
        Assert.Contains("PrivateKey", ex.Message);
        Assert.Contains("PublicKey", ex.Message);
    }

    [Fact]
    public void Parse_OnlyPrivateKey_ThrowsFormatException()
    {
        // Только PrivateKey, PublicKey отсутствует — это ошибка формата.
        const string output = "PrivateKey: abc123";

        Assert.Throws<FormatException>(() => XrayConfigParams.ParseX25519Output(output));
    }

    [Fact]
    public void Parse_OnlyPublicKey_ThrowsFormatException()
    {
        // Только PublicKey (Password), PrivateKey отсутствует — это ошибка.
        const string output = "Password (PublicKey): abc123";

        Assert.Throws<FormatException>(() => XrayConfigParams.ParseX25519Output(output));
    }

    [Fact]
    public void Parse_HandlesCrlfLineEndings()
    {
        // SSH может вернуть \r\n на Windows-серверах. Split должен
        // обработать оба варианта.
        const string output = "PrivateKey: abc\r\nPassword (PublicKey): def\r\n";

        var (priv, pub) = XrayConfigParams.ParseX25519Output(output);

        Assert.Equal("abc", priv);
        Assert.Equal("def", pub);
    }

    [Fact]
    public void Parse_PreservesBase64SpecialCharsInKey()
    {
        // x25519 PublicKey может содержать +, /, = (base64 padding).
        // Парсер не должен их обрезать или экранировать.
        const string output = """
            PrivateKey: a+b/c=d/e
            Password: x+y/z=w=
            """;

        var (priv, pub) = XrayConfigParams.ParseX25519Output(output);

        Assert.Equal("a+b/c=d/e", priv);
        Assert.Equal("x+y/z=w=", pub);
    }

    [Fact]
    public void Parse_NewFormat_IgnoresHash32()
    {
        // Hash32 — третья строка в новом формате. Парсер должен её
        // проигнорировать, не пытаться парсить как PublicKey.
        const string output = """
            PrivateKey: priv
            Password: pub
            Hash32: hash
            Extra: ignored
            Noise: ignored
            """;

        var (priv, pub) = XrayConfigParams.ParseX25519Output(output);

        Assert.Equal("priv", priv);
        Assert.Equal("pub", pub);
    }
}
