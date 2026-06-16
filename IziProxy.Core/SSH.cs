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

                var privateKey = new PrivateKeyFile(sshKeyPath);
                var keyAuth = new PrivateKeyAuthenticationMethod(serverConfig.Username, privateKey);

                connectionInfo = new ConnectionInfo(serverConfig.Host, serverConfig.Username, keyAuth)
                {
                    Timeout = TimeSpan.FromSeconds(5)
                };
            }
            else
            {
                connectionInfo = new ConnectionInfo(serverConfig.Host, serverConfig.Username,
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

                // Инициализация и подключение SFTP
                _sftpClient = new SftpClient(connectionInfo);
                _sftpClient.Connect();
            });

            return true;
        }
        catch (Exception ex)
        {
            progress?.Report("Ошибка подключения: " + ex.Message);
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
        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            progress?.Report("SFTP-клиент не подключен.");
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

            progress?.Report("MainInstall.sh загружен успешно.");
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report(ex.Message);
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
        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            progress?.Report("SFTP-клиент не подключен.");
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
                progress?.Report($"Файл {localFilePath} загружен успешно в {targetPath}");
            });

            return true;
        }
        catch (Exception ex)
        {
            progress?.Report(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Загружает поток данных на удаленный сервер через SFTP (для embedded resources).
    /// </summary>
    public async Task<bool> UploadFile(Stream stream, string remoteFileName, ServerConfig serverConfig, IProgress<string>? progress = null)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            progress?.Report("SFTP-клиент не подключен.");
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
                progress?.Report($"{remoteFileName} загружен успешно в {targetPath}");
            });

            return true;
        }
        catch (Exception ex)
        {
            progress?.Report(ex.Message);
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
        if (_sshClient == null || !_sshClient.IsConnected)
        {
            progress?.Report("SSH-клиент не подключен.");
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
                progress?.Report($"[DEBUG] Ошибки MainInstall.sh (stderr):\n{sshCommand.Error}");
            }
            
            progress?.Report(sshCommand.Result);
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report(ex.Message);
            return false;
        }
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
        if (_sshClient == null || !_sshClient.IsConnected)
            throw new InvalidOperationException("SSH-клиент не подключен.");

        // Root: выполняем как есть, без обёрток.
        if (serverConfig.Username.Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            return await Task.Run(() => _sshClient.RunCommand(command), cancellationToken);
        }

        // Не-root: используем sudo с паролем через stdin. Промпт sudo отключаем
        // флагом -p '' — нам не нужно видеть "[sudo] password for user:", иначе он
        // смешается с stdout команды и сломает парсинг результата.
        // Аргумент bash -c всегда в одинарных кавычках, экранируем вложенные '.
        string bashArg = "'" + command.Replace("'", "'\\''") + "'";
        string sudoCommand = $"sudo -S -p '' bash -c {bashArg}";

        // Прогресс-репортер оборачиваем, чтобы пароль (если случайно попадёт в
        // строку лога через ex.Message, ssh error и т.п.) был замаскирован.
        var maskedProgress = progress == null ? null : new PasswordMasker(progress, serverConfig.Password);
        maskedProgress?.Report("[DEBUG] sudo через stdin (пароль НЕ в shell-истории сервера)");

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
                    _sshClient.Disconnect();
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
                    _sftpClient.Disconnect();
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