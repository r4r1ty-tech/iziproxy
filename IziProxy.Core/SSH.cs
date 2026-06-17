using Renci.SshNet;

namespace IziProxy;

/// <summary>
/// Управляет SSH- и SFTP-подключениями к удаленному серверу, а также выполнением команд и передачей файлов.
/// </summary>
public class SSH : IDisposable
{
    private SshClient _sshClient = null!;
    private SftpClient _sftpClient = null!;
    private bool _disposed;

    /// <summary>
    /// Проверяет подключение к серверу по протоколам SSH и SFTP.
    /// </summary>
    /// <param name="serverConfig">Конфигурация с хостом, логином и паролем.</param>
    /// <param name="progress">Получатель прогресса и сообщений об ошибках.</param>
    /// <returns>True, если оба подключения успешно установлены; иначе false.</returns>
    public async Task<bool> TestConnection(ServerConfig serverConfig, IProgress<string>? progress = null)
    {
        progress?.Report($"[TRACE] SSH.TestConnection вход: host={serverConfig.Host}, port={serverConfig.Port}, user={serverConfig.Username}, auth={(string.IsNullOrEmpty(serverConfig.SshKey) ? "password" : "key")}");

        try
        {
            ConnectionInfo connectionInfo;

            if (!string.IsNullOrEmpty(serverConfig.SshKey))
            {
                string sshKeyPath = serverConfig.SshKey;
                if (sshKeyPath.StartsWith("~"))
                {
                    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    sshKeyPath = Path.Combine(userProfile, sshKeyPath.TrimStart('~', '/', '\\'));
                }
                progress?.Report($"[DEBUG] SSH.TestConnection: используем ключ {sshKeyPath}");

                var privateKey = new PrivateKeyFile(sshKeyPath);
                var keyAuth = new PrivateKeyAuthenticationMethod(serverConfig.Username, privateKey);

                connectionInfo = new ConnectionInfo(serverConfig.Host, serverConfig.Port, serverConfig.Username, keyAuth)
                    {
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                }
                else
                {
                progress?.Report("[DEBUG] SSH.TestConnection: используем password-аутентификацию");
                connectionInfo = new ConnectionInfo(serverConfig.Host, serverConfig.Port, serverConfig.Username,
                    new PasswordAuthenticationMethod(serverConfig.Username, serverConfig.Password))
                    {
                        Timeout = TimeSpan.FromSeconds(5)
                    };
                }

            await Task.Run(() =>
            {
                // Инициализация и подключение SSH
                _sshClient = new SshClient(connectionInfo);
                _sshClient.Connect();
                progress?.Report("[DEBUG] SSH-клиент подключен");

                // Инициализация и подключение SFTP
                _sftpClient = new SftpClient(connectionInfo);
                _sftpClient.Connect();
                progress?.Report("[DEBUG] SFTP-клиент подключен");
            });

            progress?.Report("[INFO] SSH-подключение установлено успешно");
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"[ERROR] Ошибка подключения: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Загружает скрипт предварительной подготовки MainInstall.sh на сервер.
    /// </summary>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <param name="progress">Получатель прогресса и сообщений об ошибках.</param>
    /// <returns>True, если загрузка прошла успешно; иначе false.</returns>
    public async Task<bool> UploadTestScript(ServerConfig serverConfig, IProgress<string>? progress = null)
    {
        progress?.Report($"[TRACE] SSH.UploadTestScript вход: host={serverConfig.Host}, user={serverConfig.Username}");

        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            progress?.Report("[ERROR] SFTP-клиент не подключен");
            return false;
        }

        try
        {
            await Task.Run(() =>
            {
                using var fileStream = EmbeddedScripts.OpenMainInstall();
                using var reader = new StreamReader(fileStream);
                string content = reader.ReadToEnd().Replace("\r\n", "\n");
                using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

                string targetPath;
                // Определяем домашнюю директорию в зависимости от имени пользователя
                if (serverConfig.Username == "root")
                    targetPath = $"/root/MainInstall.sh";
                else
                    targetPath = $"/home/{serverConfig.Username}/MainInstall.sh";

                progress?.Report($"[DEBUG] SFTP Uploading MainInstall.sh to {targetPath} (размер потока: {ms.Length} байт, сконвертирован в LF)");
                _sftpClient.UploadFile(ms, targetPath);
            });

            progress?.Report("[INFO] MainInstall.sh загружен успешно");
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"[ERROR] Ошибка загрузки MainInstall.sh: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Загружает локальный файл на удаленный сервер через SFTP с возможностью перезаписи.
    /// </summary>
    /// <param name="localFilePath">Путь к файлу на локальном компьютере.</param>
    /// <param name="remoteFileName">Имя файла, под которым он будет сохранен на сервере.</param>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <param name="progress">Получатель прогресса и сообщений об ошибках.</param>
    /// <returns>True, если файл успешно загружен; иначе false.</returns>
    public async Task<bool> UploadFile(string localFilePath, string remoteFileName, ServerConfig serverConfig, IProgress<string>? progress = null)
    {
        progress?.Report($"[TRACE] SSH.UploadFile вход: local={localFilePath}, remote={remoteFileName}");

        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            progress?.Report("[ERROR] SFTP-клиент не подключен");
            return false;
        }

        try
        {
            await Task.Run(() =>
            {
                string content = File.ReadAllText(localFilePath).Replace("\r\n", "\n");
                using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

                string targetPath;
                if (serverConfig.Username == "root")
                    targetPath = $"/root/{remoteFileName}";
                else
                    targetPath = $"/home/{serverConfig.Username}/{remoteFileName}";

                progress?.Report($"[DEBUG] SFTP Uploading {localFilePath} to {targetPath} (размер файла: {ms.Length} байт, сконвертирован в LF)");
                _sftpClient.UploadFile(ms, targetPath, true); // true = overwrite (перезаписать при наличии)
                progress?.Report($"[INFO] Файл {localFilePath} загружен успешно в {targetPath}");
            });

            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"[ERROR] Ошибка загрузки файла {localFilePath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Загружает поток данных на удаленный сервер через SFTP (для embedded resources).
    /// </summary>
    public async Task<bool> UploadFile(Stream stream, string remoteFileName, ServerConfig serverConfig, IProgress<string>? progress = null)
    {
        progress?.Report($"[TRACE] SSH.UploadFile(Stream) вход: remote={remoteFileName}");

        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            progress?.Report("[ERROR] SFTP-клиент не подключен");
            return false;
        }

        try
        {
            await Task.Run(() =>
            {
                using var reader = new StreamReader(stream);
                string content = reader.ReadToEnd().Replace("\r\n", "\n");
                using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

                string targetPath;
                if (serverConfig.Username == "root")
                    targetPath = $"/root/{remoteFileName}";
                else
                    targetPath = $"/home/{serverConfig.Username}/{remoteFileName}";

                progress?.Report($"[DEBUG] SFTP Uploading stream to {targetPath} (размер потока: {ms.Length} байт, сконвертирован в LF)");
                _sftpClient.UploadFile(ms, targetPath, true);
                progress?.Report($"[INFO] {remoteFileName} загружен успешно в {targetPath}");
            });

            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"[ERROR] Ошибка загрузки потока в {remoteFileName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Запускает скрипт предварительной подготовки MainInstall.sh на удаленном сервере.
    /// </summary>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <param name="progress">Получатель прогресса и сообщений об ошибках.</param>
    /// <returns>True, если скрипт успешно запущен; иначе false.</returns>
    public async Task<bool> RunTestScript(ServerConfig serverConfig, IProgress<string>? progress = null)
    {
        progress?.Report($"[TRACE] SSH.RunTestScript вход: host={serverConfig.Host}, user={serverConfig.Username}");

        if (_sshClient == null || !_sshClient.IsConnected)
        {
            progress?.Report("[ERROR] SSH-клиент не подключен");
            return false;
        }

        try
        {
            string homeDir = serverConfig.Username == "root" ? "/root" : $"/home/{serverConfig.Username}";
            string command = $"chmod +x {homeDir}/MainInstall.sh && bash {homeDir}/MainInstall.sh";

            progress?.Report($"[DEBUG] Выполнение MainInstall.sh: {command}");
            SshCommand sshCommand = await RunSudoCommand(serverConfig, command);

            if (!string.IsNullOrWhiteSpace(sshCommand.Error))
            {
                progress?.Report($"[WARN] Ошибки MainInstall.sh (stderr):\n{sshCommand.Error}");
            }

            progress?.Report(sshCommand.Result);
            progress?.Report("[INFO] MainInstall.sh выполнен");
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"[ERROR] Ошибка выполнения MainInstall.sh: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Формирует финальную строку команды, которую <c>_sshClient.RunCommand</c>
    /// отправит на сервер. Под root команда выполняется как есть,
    /// для остальных — оборачивается в <c>sudo -S -p '' bash -c '...'</c> с
    /// правильным POSIX-escape одинарных кавычек.
    /// </summary>
    /// <remarks>
    /// Выделен в отдельный internal-метод чтобы его можно было покрыть
    /// юнит-тестами без поднятия sshd (см. <c>BuildSudoCommandTests</c>).
    /// Пароль через stdin подаётся отдельно в <see cref="RunSudoCommand"/>;
    /// здесь только формирование shell-строки.
    /// </remarks>
    /// <param name="username">Имя пользователя SSH (если "root" — без sudo-обвязки).</param>
    /// <param name="command">Команда, которую нужно выполнить.</param>
    /// <returns>Готовая строка для <c>SshClient.RunCommand</c> или <c>CreateCommand</c>.</returns>
    public static string BuildSudoCommand(string username, string command)
    {
        if (username.Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            return command;
        }

        // Аргумент bash -c всегда в одинарных кавычках. Внутри single-quoted
        // строки одинарная кавычка экранируется через end-quote, escaped-quote,
        // start-quote: '\'' . Это POSIX-standard, работает в bash/dash/zsh.
        // Пример: "echo 'hello'" → "'echo '\\''hello'\\'''"
        string bashArg = "'" + command.Replace("'", "'\\''") + "'";
        return $"sudo -S -p '' bash -c {bashArg}";
    }

    /// <summary>
    /// Выполняет команду с правами администратора (sudo) на сервере.
    /// Автоматически подставляет пароль пользователя при необходимости (если вход выполнен не под root).
    /// </summary>
    /// <remarks>
    /// Пароль передаётся через stdin команды <c>sudo -S</c>, а не через <c>echo</c> в shell —
    /// это исключает попадание пароля в историю команд и в вывод <c>ps</c> на сервере.
    /// Перед вызовом прогресс-репортер оборачивается в <see cref="PasswordMasker"/>, чтобы
    /// пароль не утек в логи клиента.
    /// </remarks>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <param name="command">Выполняемая команда.</param>
    /// <param name="progress">Получатель прогресса и сообщений об ошибках (автоматически маскируется).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Объект <see cref="SshCommand"/> с результатом выполнения команды.</returns>
    /// <exception cref="InvalidOperationException">Бросается, если клиент не подключен.</exception>
    public async Task<SshCommand> RunSudoCommand(
        ServerConfig serverConfig,
        string command,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report($"[TRACE] SSH.RunSudoCommand вход: user={serverConfig.Username}, command={command}");

        if (_sshClient == null || !_sshClient.IsConnected)
        {
            progress?.Report("[ERROR] SSH-клиент не подключен");
            throw new InvalidOperationException("SSH-клиент не подключен.");
        }

        // Root: выполняем как есть, без обёрток.
        if (serverConfig.Username.Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report("[DEBUG] RunSudoCommand: root-пользователь, выполняем без sudo-обвязки");
            return await Task.Run(() => _sshClient.RunCommand(command), cancellationToken);
        }

        // Не-root: используем sudo с паролем через stdin. Промпт sudo отключаем
        // флагом -p '' — нам не нужно видеть "[sudo] password for user:", иначе он
        // смешается с stdout команды и сломает парсинг результата. Формирование
        // shell-строки вынесено в BuildSudoCommand для покрытия юнит-тестами.
        string sudoCommand = BuildSudoCommand(serverConfig.Username, command);

        // Прогресс-репортер оборачиваем, чтобы пароль (если случайно попадёт в
        // строку лога через ex.Message, ssh error и т.п.) был замаскирован.
        var maskedProgress = progress == null ? null : new PasswordMasker(progress, serverConfig.Password);
        maskedProgress?.Report("[DEBUG] sudo через stdin (пароль НЕ в shell-истории сервера)");
        maskedProgress?.Report($"[DEBUG] RunSudoCommand: shell-команда: {sudoCommand}");

        return await Task.Run(() =>
        {
            using var sshCommand = _sshClient.CreateCommand(sudoCommand);
            using (var input = sshCommand.CreateInputStream())
            {
                // Запускаем асинхронно и параллельно пишем пароль. SSH.NET
                // буферизует ввод — даже если sudo ещё не начал читать, пароль
                // будет ждать в pipe.
                var executeTask = sshCommand.ExecuteAsync(cancellationToken);
                var passwordBytes = System.Text.Encoding.UTF8.GetBytes(serverConfig.Password + "\n");
                input.Write(passwordBytes, 0, passwordBytes.Length);
                // Закрытие stream (через using) сообщает sudo, что stdin кончился —
                // после прочтения пароля sudo запустит bash, а bash увидит EOF на stdin
                // и команда выполнится без блокировки.
                executeTask.Wait(cancellationToken);
            }
            maskedProgress?.Report($"[DEBUG] RunSudoCommand: exit={sshCommand.ExitStatus}, длина stdout={sshCommand.Result?.Length ?? 0}, длина stderr={sshCommand.Error?.Length ?? 0}");
            return sshCommand;
        }, cancellationToken);
    }

    /// <summary>
    /// Освобождает ресурсы, закрывая SSH и SFTP подключения.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_sshClient != null)
        {
            try
            {
                if (_sshClient.IsConnected)
                {
                    _sshClient.Disconnect();
                    System.Diagnostics.Debug.WriteLine("[DEBUG] SSH.Disconnect: SSH-клиент отключен");
                }
            }
            catch (ObjectDisposedException) { }

            try
            {
                _sshClient.Dispose();
            }
            catch (ObjectDisposedException) { }
        }

        if (_sftpClient != null)
        {
            try
            {
                if (_sftpClient.IsConnected)
                {
                    _sftpClient.Disconnect();
                    System.Diagnostics.Debug.WriteLine("[DEBUG] SSH.Disconnect: SFTP-клиент отключен");
                }
            }
            catch (ObjectDisposedException) { }

            try
            {
                _sftpClient.Dispose();
            }
            catch (ObjectDisposedException) { }
        }
    }
}