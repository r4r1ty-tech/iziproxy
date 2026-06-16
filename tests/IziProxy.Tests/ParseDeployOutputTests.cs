using Xunit;

namespace IziProxy.Tests;

/// <summary>
/// Тесты для <see cref="DeployScripts.ParseDeployOutput"/> (F-07 из FutureTest.md).
/// Покрывают: happy path с тремя inbound'ами, пустой вывод, частичный
/// вывод (только порты, только SNI), пробелы/Trailing whitespace,
/// CRLF окончания строк, лишние строки в выводе.
/// </summary>
public class ParseDeployOutputTests
{
    [Fact]
    public void Parse_HappyPath_ExtractsAllSixValues()
    {
        const string output = """
            Setting up ports...
            SELECTED_PORT_1=443
            SELECTED_PORT_2=8443
            SELECTED_PORT_3=50001
            Picking best SNI...
            SNI_SELECTED_1=cdn.example.com
            SNI_SELECTED_2=static.example.com
            SNI_SELECTED_3=speed.example.com
            Done.
            """;

        var result = DeployScripts.ParseDeployOutput(output);

        Assert.Equal("443", result.Port1);
        Assert.Equal("8443", result.Port2);
        Assert.Equal("50001", result.Port3);
        Assert.Equal("cdn.example.com", result.Sni1);
        Assert.Equal("static.example.com", result.Sni2);
        Assert.Equal("speed.example.com", result.Sni3);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsAllEmpty()
    {
        var result = DeployScripts.ParseDeployOutput("");

        Assert.Equal("", result.Port1);
        Assert.Equal("", result.Port2);
        Assert.Equal("", result.Port3);
        Assert.Equal("", result.Sni1);
        Assert.Equal("", result.Sni2);
        Assert.Equal("", result.Sni3);
    }

    [Fact]
    public void Parse_NullInput_ReturnsAllEmpty()
    {
        var result = DeployScripts.ParseDeployOutput(null!);

        Assert.Equal("", result.Port1);
        Assert.Equal("", result.Sni2);
    }

    [Fact]
    public void Parse_OnlyPortsSet_SnisRemainEmpty()
    {
        const string output = """
            SELECTED_PORT_1=443
            SELECTED_PORT_2=8443
            SELECTED_PORT_3=50001
            """;

        var result = DeployScripts.ParseDeployOutput(output);

        Assert.Equal("443", result.Port1);
        Assert.Equal("8443", result.Port2);
        Assert.Equal("50001", result.Port3);
        Assert.Equal("", result.Sni1);
        Assert.Equal("", result.Sni2);
        Assert.Equal("", result.Sni3);
    }

    [Fact]
    public void Parse_OnlySnisSet_PortsRemainEmpty()
    {
        const string output = """
            SNI_SELECTED_1=cdn.example.com
            SNI_SELECTED_2=static.example.com
            SNI_SELECTED_3=speed.example.com
            """;

        var result = DeployScripts.ParseDeployOutput(output);

        Assert.Equal("", result.Port1);
        Assert.Equal("cdn.example.com", result.Sni1);
    }

    [Fact]
    public void Parse_MissingMiddlePort_Port2IsEmpty()
    {
        // Sкрипт вернул только Port1 и Port3 (теоретически — bug в скрипте).
        // Парсер должен дать Port2="" и caller упадёт на валидации.
        const string output = """
            SELECTED_PORT_1=443
            SELECTED_PORT_3=50001
            """;

        var result = DeployScripts.ParseDeployOutput(output);

        Assert.Equal("443", result.Port1);
        Assert.Equal("", result.Port2);   // пропущен
        Assert.Equal("50001", result.Port3);
    }

    [Fact]
    public void Parse_TrailingWhitespace_IsTrimmed()
    {
        // "SELECTED_PORT_1=443   " — парсер должен Trim()'ить хвостовые пробелы.
        // Реально Deploy.sh добавляет ANSI-цвета, которые потом видны как
        // пробелы после Trim (если bash скрипт не использует 'echo -n').
        const string output = """
            SELECTED_PORT_1=443
            SELECTED_PORT_2=8443   
            SELECTED_PORT_3=	50001
            SNI_SELECTED_1=cdn.example.com
            SNI_SELECTED_2=static.example.com
            SNI_SELECTED_3=speed.example.com
            """;

        var result = DeployScripts.ParseDeployOutput(output);

        Assert.Equal("443", result.Port1);
        Assert.Equal("8443", result.Port2);
        Assert.Equal("50001", result.Port3);
    }

    [Fact]
    public void Parse_CrlfLineEndings_Handled()
    {
        // SSH-клиент на Windows-серверах может прислать \r\n. Split с
        // обоими разделителями должен корректно обработать.
        const string output = "SELECTED_PORT_1=443\r\nSELECTED_PORT_2=8443\r\nSELECTED_PORT_3=50001\r\n" +
                              "SNI_SELECTED_1=cdn.example.com\r\nSNI_SELECTED_2=static.example.com\r\nSNI_SELECTED_3=speed.example.com\r\n";

        var result = DeployScripts.ParseDeployOutput(output);

        Assert.Equal("443", result.Port1);
        Assert.Equal("speed.example.com", result.Sni3);
    }

    [Fact]
    public void Parse_DuplicatePortLines_LastWins()
    {
        // Если скрипт случайно вывел SELECTED_PORT_1 дважды — берётся
        // последний (текущая реализация: просто перезаписывает значение).
        const string output = """
            SELECTED_PORT_1=443
            SELECTED_PORT_1=50001
            SELECTED_PORT_2=8443
            SELECTED_PORT_3=50002
            SNI_SELECTED_1=cdn.example.com
            SNI_SELECTED_2=static.example.com
            SNI_SELECTED_3=speed.example.com
            """;

        var result = DeployScripts.ParseDeployOutput(output);

        Assert.Equal("50001", result.Port1); // последний wins
    }

    [Fact]
    public void Parse_NoSelectedKeys_ReturnsAllEmpty()
    {
        // Bash скрипт упал до того, как вывести SELECTED_* — вывод есть,
        // но без наших ключей.
        const string output = """
            bash: line 1: jq: command not found
            Error: cannot parse SNI
            Aborted.
            """;

        var result = DeployScripts.ParseDeployOutput(output);

        Assert.Equal("", result.Port1);
        Assert.Equal("", result.Port2);
        Assert.Equal("", result.Port3);
        Assert.Equal("", result.Sni1);
        Assert.Equal("", result.Sni2);
        Assert.Equal("", result.Sni3);
    }

    [Fact]
    public void Parse_SimilarPrefix_NotConfusedWithSni()
    {
        // "SELECTED_PORT_SOMETHING=" не должен парситься как Port1.
        // Только точные префиксы SELECTED_PORT_1=, _2=, _3=.
        const string output = """
            SELECTED_PORT_1=443
            SELECTED_PORT_10=99999
            SELECTED_PORT_2=8443
            SNI_SELECTED_1=cdn.example.com
            SNI_SELECTED_10=junk
            SNI_SELECTED_2=static.example.com
            SELECTED_PORT_3=50001
            SNI_SELECTED_3=speed.example.com
            """;

        var result = DeployScripts.ParseDeployOutput(output);

        // _10 не должно сматчиться
        Assert.Equal("443", result.Port1);
        Assert.Equal("8443", result.Port2);
        Assert.Equal("50001", result.Port3);
        Assert.Equal("cdn.example.com", result.Sni1);
        Assert.Equal("static.example.com", result.Sni2);
        Assert.Equal("speed.example.com", result.Sni3);
    }

    [Fact]
    public void Parse_EmptyValue_AfterEquals_IsKeptAsEmpty()
    {
        // "SELECTED_PORT_1=" (пустое значение после =) — Trim() даёт "",
        // парсер сохраняет. Caller упадёт на валидации.
        const string output = """
            SELECTED_PORT_1=
            SELECTED_PORT_2=8443
            SELECTED_PORT_3=50001
            SNI_SELECTED_1=cdn.example.com
            SNI_SELECTED_2=static.example.com
            SNI_SELECTED_3=speed.example.com
            """;

        var result = DeployScripts.ParseDeployOutput(output);

        Assert.Equal("", result.Port1);
    }
}
