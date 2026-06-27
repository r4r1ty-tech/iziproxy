using System;
using System.IO;
using System.Threading.Tasks;
using IziProxy;
using Xunit;
using Xunit.Abstractions;
using static Xunit.Skip;

namespace IziProxy.Tests;

/// <summary>
/// Integration-тесты для SSH (F-01 из FutureTest.md).
/// Требуют живого sshd. Запускаются ТОЛЬКО если установлена переменная
/// окружения <c>TEST_SSHD_HOST</c> (формат <c>host:port</c>), иначе
/// skip'аются с подсказкой как поднять sshd локально.
/// </summary>
/// <remarks>
/// <para>Как поднять sshd на Fedora для прогона тестов:</para>
/// <code>
/// # 1. Сгенерировать ключ (один раз)
/// ssh-keygen -t rsa -b 2048 -N "" -f /tmp/izitest/id_rsa -q
///
/// # 2. Создать конфиг /tmp/izitest/sshd_config (см. scripts/setup_test_sshd.sh)
/// # 3. Запустить sshd на 2222
/// /usr/sbin/sshd -f /tmp/izitest/sshd_config
///
/// # 4. Установить env vars и прогнать тесты
/// export TEST_SSHD_HOST=127.0.0.1:2222
/// export TEST_SSHD_USER=alex
/// export TEST_SSHD_KEY=/tmp/izitest/id_rsa
/// dotnet test --filter "SshIntegrationTests"
/// </code>
/// <para>
/// В CI (GitHub Actions) — поднять service container с `linuxserver/openssh-server`
/// и пробросить ключ через secrets/ci-key.
/// </para>
/// </remarks>
public class SshIntegrationTests
{
    private const string SshHostEnv = "TEST_SSHD_HOST";        // "host:port"
    private const string SshUserEnv = "TEST_SSHD_USER";        // "username"
    private const string SshKeyEnv  = "TEST_SSHD_KEY";         // "/path/to/id_rsa"

    private static readonly Lazy<bool> _available = new(CheckAvailable);

    private static bool CheckAvailable()
    {
        string? host = Environment.GetEnvironmentVariable(SshHostEnv);
        string? user = Environment.GetEnvironmentVariable(SshUserEnv);
        string? key  = Environment.GetEnvironmentVariable(SshKeyEnv);

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (!File.Exists(key))
        {
            return false;
        }

        // Hostname:port → (host, port)
        string[] parts = host!.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out _))
        {
            return false;
        }

        return true;
    }

    private static ServerConfig BuildConfig()
    {
        string host = Environment.GetEnvironmentVariable(SshHostEnv)!;
        string user = Environment.GetEnvironmentVariable(SshUserEnv)!;
        string key  = Environment.GetEnvironmentVariable(SshKeyEnv)!;

        string[] parts = host.Split(':', 2);
        int port = 22;
        if (parts.Length == 2 && int.TryParse(parts[1], out int p))
        {
            port = p;
        }

        return new ServerConfig
        {
            Host = parts[0],
            Port = port,
            Username = user,
            Password = string.Empty,           // используем ключ, не пароль
            SshKey = key,
        };
    }

    private static void SkipIfUnavailable()
    {
        if (!_available.Value)
        {
            Skip.IfNot(_available.Value,
                $"Integration test requires env vars: " +
                $"{SshHostEnv}=host:port, {SshUserEnv}=username, {SshKeyEnv}=path/to/key. " +
                $"См. XML-doc на SshIntegrationTests для setup.");
        }
    }

    [SkippableFact]
    public async Task TestConnection_WithValidKey_ReturnsTrue()
    {
        SkipIfUnavailable();

        using var ssh = new SSH();
        var progress = new TestProgress();

        bool result = await ssh.TestConnection(BuildConfig(), progress);

        Assert.True(result,
            $"TestConnection должен вернуть true с валидным ключом. " +
            $"Progress: {progress.AllText}");
    }

    [SkippableFact]
    public async Task TestConnection_WithWrongKeyFile_ThrowsOrReturnsFalse()
    {
        SkipIfUnavailable();

        // Подсовываем несуществующий ключ — SSH.NET должен бросить
        // exception или вернуть false (через progress с сообщением).
        var cfg = BuildConfig();
        cfg.SshKey = "/tmp/izitest-sshd/this-key-does-not-exist";

        using var ssh = new SSH();
        var progress = new TestProgress();

        // SSH.NET может бросить на этапе загрузки ключа ИЛИ вернуть false.
        // Главное — НЕ зависнуть и НЕ вернуть true.
        bool result;
        try
        {
            result = await ssh.TestConnection(cfg, progress);
        }
        catch (Exception)
        {
            // Тоже допустимо — exception проглатывается в TestConnection,
            // но если SSH.NET бросает до этого, мы тоже считаем тест
            // валидно прошедшим (главное что не подвисло).
            return;
        }

        Assert.False(result, "TestConnection должен вернуть false или бросить с невалидным ключом");
    }

    [SkippableFact]
    public async Task TestConnection_WithWrongPort_TimesOutOrRefused()
    {
        SkipIfUnavailable();

        // Подключаемся к заведомо неправильному порту (1) — на Linux не слушает
        // ничего кроме нашего 2222. Должно либо connection refused, либо
        // timeout. Главное — НЕ зависнуть.
        var cfg = BuildConfig();
        cfg.Host = cfg.Host; // тот же host
        cfg.Username = Environment.GetEnvironmentVariable(SshUserEnv) ?? string.Empty;

        // Используем подмену host:port через прямое указание
        string originalHost = Environment.GetEnvironmentVariable(SshHostEnv)!;
        try
        {
            Environment.SetEnvironmentVariable(SshHostEnv, "127.0.0.1:1");
            using var ssh = new SSH();
            var progress = new TestProgress();

            bool result = await ssh.TestConnection(BuildConfig(), progress);
            Assert.False(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SshHostEnv, originalHost);
        }
    }

    [SkippableFact]
    public async Task TestConnection_DoesNotLeakPasswordInErrorMessage()
    {
        SkipIfUnavailable();

        // Security-инвариант (ISS-01): даже если подключение упало,
        // ex.Message НЕ должен содержать пароль. Тест подменяет
        // serverConfig.Password через DataAnnotation — но ServerConfig
        // это POCO, поэтому просто создаём отдельный конфиг.
        var cfg = BuildConfig();
        cfg.Password = "SuperSecret123!@#"; // пароль не используется (ключ есть),
                                            // но если SSH.NET его сериализует в error — утечка

        using var ssh = new SSH();
        var progress = new TestProgress();

        // Подключение должно пройти (ключ есть, пароль игнорируется)
        // или упасть — но НЕ с паролем в error.
        bool result = await ssh.TestConnection(cfg, progress);
        Assert.True(result);

        // Проверяем что в progress-логе нет пароля
        Assert.DoesNotContain("SuperSecret123!@#", progress.AllText);
    }

    [SkippableFact]
    public async Task RunSudoCommand_AsRoot_ExecutesWithoutSudoWrapper()
    {
        SkipIfUnavailable();

        // Security-инвариант (ISS-01, F-02): под root команда выполняется
        // напрямую, без sudo-обвязки. Тест: запускаем 'whoami' под пользователем,
        // который зашёл на sshd как root (или кто может sudo).
        // Для упрощения — устанавливаем Username='root' и проверяем что
        // команда возвращает 'root' (а не 'sudo: ...').
        var cfg = BuildConfig();
        cfg.Username = "root";

        using var ssh = new SSH();
        var progress = new TestProgress();

        // Пропускаем TestConnection (нужен ключ для root) — сразу проверяем
        // BuildSudoCommand косвенно через сам факт подключения + команду.
        bool connected = await ssh.TestConnection(cfg, progress);
        if (!connected)
        {
            // root может быть недоступен по SSH (PermitRootLogin prohibit-password).
            // В этом случае тест skip'ается по SSH-уровню, не по assert-уровню.
            Skip.IfNot(false, "root недоступен по SSH на тестовом сервере — тест BuildSudoCommand под root невозможен здесь");
            return;
        }

        var result = await ssh.RunSudoCommand(cfg, "whoami", progress);
        Assert.Equal(0, result.ExitStatus);
        Assert.Contains("root", result.Result.Trim());
    }

    // Простой IProgress<string> для тестов — собирает все сообщения.
    private sealed class TestProgress : IProgress<string>
    {
        public string AllText { get; private set; } = string.Empty;

        public void Report(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                AllText += value + "\n";
            }
        }
    }
}
