using Xunit;

namespace IziProxy.Tests;

/// <summary>
/// Тесты для <see cref="SSH.BuildSudoCommand"/> (F-02 + F-03 из FutureTest.md).
/// Покрывают: путь root (без sudo-обвязки), путь non-root с простой командой,
/// escape одинарных кавычек по POSIX, edge cases с пустой/Unicode-командой.
/// </summary>
public class BuildSudoCommandTests
{
    [Fact]
    public void Root_PassesCommandThroughUnchanged()
    {
        // Под root команда выполняется напрямую — никакой sudo-обёртки,
        // никакого bash -c. Это security-инвариант: если бы мы добавляли
        // sudo для root, он бы падал (sudo: нечего делать для root).
        Assert.Equal("ls -la", SSH.BuildSudoCommand("root", "ls -la"));
        Assert.Equal("systemctl restart xray", SSH.BuildSudoCommand("root", "systemctl restart xray"));
    }

    [Fact]
    public void Root_UsernameIsCaseInsensitive()
    {
        // "Root", "ROOT", "rOOt" — всё должно идти без sudo. На большинстве
        // VDS учётка root написана именно в нижнем регистре, но если кто-то
        // ввёл "Root" в ServerConfig.Username — мы не должны падать.
        Assert.Equal("whoami", SSH.BuildSudoCommand("Root", "whoami"));
        Assert.Equal("whoami", SSH.BuildSudoCommand("ROOT", "whoami"));
        Assert.Equal("whoami", SSH.BuildSudoCommand("rOOt", "whoami"));
    }

    [Fact]
    public void NonRoot_WrapsInSudoBashC()
    {
        // Проверяем точную формулу: 'sudo -S -p '' bash -c '<command>''.
        // -S: читать пароль из stdin. -p '': не показывать "[sudo] password for user:"
        // (иначе он смешается с stdout команды и сломает парсинг Deploy.sh).
        Assert.Equal("sudo -S -p '' bash -c 'ls'", SSH.BuildSudoCommand("user", "ls"));
        Assert.Equal("sudo -S -p '' bash -c 'whoami'", SSH.BuildSudoCommand("deploy", "whoami"));
    }

    [Fact]
    public void NonRoot_PreservesSpecialCharsOutsideQuotes()
    {
        // Символы $, |, ;, &&, *, >, <, ~, # внутри команды НЕ экранируются
        // дополнительно — они в single-quoted строке bash, где не интерпретируются.
        // Мы только добавляем single-quote-обёртку.
        Assert.Equal("sudo -S -p '' bash -c 'echo $HOME | grep root'",
            SSH.BuildSudoCommand("user", "echo $HOME | grep root"));
        Assert.Equal("sudo -S -p '' bash -c 'rm -rf /tmp/* && echo done'",
            SSH.BuildSudoCommand("user", "rm -rf /tmp/* && echo done"));
    }

    [Fact]
    public void NonRoot_HandlesSingleQuoteViaPosixEscape()
    {
        // POSIX-escape одинарной кавычки внутри single-quoted строки:
        // закрыть-quот, экранированная-кавычка, открыть-квоту: '\''
        // Пример: "echo 'hello'" → "sudo -S -p '' bash -c 'echo '\''hello'\'''"
        //                                                                             ^^
        //                                                                             end-open
        string result = SSH.BuildSudoCommand("user", "echo 'hello'");
        Assert.Equal("sudo -S -p '' bash -c 'echo '\\''hello'\\'''", result);
    }

    [Fact]
    public void NonRoot_HandlesMultipleSingleQuotes()
    {
        // "ls /root/it's mine" → каждая ' экранируется отдельно.
        // "ls /root/it'\''s mine" в нашем формате:
        // 'ls /root/it' + '\'' + 's mine'
        string result = SSH.BuildSudoCommand("user", "ls /root/it's mine");
        Assert.Equal("sudo -S -p '' bash -c 'ls /root/it'\\''s mine'", result);
    }

    [Fact]
    public void NonRoot_HandlesEmptyCommand()
    {
        // Edge case: пустая команда → sudo -S -p '' bash -c '' (валидно, bash
        // выполнит "пустую" команду, ничего не произойдёт). Не должно
        // бросать exception.
        Assert.Equal("sudo -S -p '' bash -c ''", SSH.BuildSudoCommand("user", ""));
    }

    [Fact]
    public void NonRoot_HandlesCommandWithLeadingSingleQuote()
    {
        // " 'foo" → "\'foo" в single-quoted форме
        string result = SSH.BuildSudoCommand("user", "'foo");
        Assert.Equal("sudo -S -p '' bash -c ''\\''foo'", result);
    }

    [Fact]
    public void NonRoot_HandlesCommandWithTrailingSingleQuote()
    {
        // "foo'" → "foo'" в single-quoted форме
        string result = SSH.BuildSudoCommand("user", "foo'");
        Assert.Equal("sudo -S -p '' bash -c 'foo'\\'''", result);
    }

    [Fact]
    public void NonRoot_HandlesCommandThatIsOnlySingleQuote()
    {
        // "'" → '\'' в single-quoted форме
        string result = SSH.BuildSudoCommand("user", "'");
        Assert.Equal("sudo -S -p '' bash -c ''\\'''", result);
    }

    [Fact]
    public void NonRoot_HandlesUnicodeCommand()
    {
        // Unicode в команде (русский текст, эмодзи) — bash с UTF-8 локалью
        // обработает корректно, мы только не должны потерять байты.
        string result = SSH.BuildSudoCommand("user", "echo 'Привет мир'");
        Assert.Equal("sudo -S -p '' bash -c 'echo '\\''Привет мир'\\'''", result);
    }

    [Fact]
    public void NonRoot_PreservesNewlinesAndTabs()
    {
        // Многострочные команды (heredoc-стиль) — кавычки экранируют \n
        // от интерпретации shell'ом, что и нужно.
        string cmd = "cat <<EOF\nline1\nline2\nEOF";
        string result = SSH.BuildSudoCommand("user", cmd);
        Assert.Equal($"sudo -S -p '' bash -c '{cmd}'", result);
    }
}
