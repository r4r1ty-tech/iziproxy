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

    public Boolean TestConnection(ServerConfig serverConfig)
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

            // Инициализация и подключение SSH
            _sshClient = new SshClient(connectionInfo);
            _sshClient.Connect();

            // Инициализация и подключение SFTP
            _sftpClient = new SftpClient(connectionInfo);
            _sftpClient.Connect();

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Загружает скрипт предварительной подготовки MainInstall.sh на сервер.
    /// </summary>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <returns>True, если загрузка прошла успешно; иначе false.</returns>
    public Boolean UploadTestScript(ServerConfig serverConfig)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            Console.WriteLine("SFTP-клиент не подключен.");
            return false;
        }

        try
        {
            using var file = File.OpenRead("VDS_setup/MainInstall.sh");
            string targetPath;
            // Определяем домашнюю директорию в зависимости от имени пользователя
            if (serverConfig.Username == "root")
            {
                targetPath = "/root/MainInstall.sh";
            }
            else
            {
                targetPath = "MainInstall.sh"; // Загрузит в домашнюю папку пользователя
            }

            _sftpClient.UploadFile(file, targetPath);
            Console.WriteLine("Файл загружен успешно");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Загружает локальный файл на удаленный сервер через SFTP с возможностью перезаписи.
    /// </summary>
    /// <param name="localFilePath">Путь к файлу на локальном компьютере.</param>
    /// <param name="remoteFileName">Имя файла, под которым он будет сохранен на сервере.</param>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <returns>True, если файл успешно загружен; иначе false.</returns>
    public Boolean UploadFile(string localFilePath, string remoteFileName, ServerConfig serverConfig)
    {
        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            Console.WriteLine("SFTP-клиент не подключен.");
            return false;
        }

        try
        {
            using var file = File.OpenRead(localFilePath);
            
            string targetPath;
            if (serverConfig.Username == "root")
            {
                targetPath = $"/root/{remoteFileName}";
            }
            else
            {
                targetPath = remoteFileName;
            }
            
            _sftpClient.UploadFile(file, targetPath, true); // true = overwrite (перезаписать при наличии)
            Console.WriteLine($"Файл {localFilePath} загружен успешно в {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Запускает скрипт предварительной подготовки MainInstall.sh на удаленном сервере.
    /// </summary>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <returns>True, если скрипт успешно запущен; иначе false.</returns>
    public Boolean RunTestScript(ServerConfig serverConfig)
    {
        if (_sshClient == null || !_sshClient.IsConnected)
        {
            Console.WriteLine("SSH-клиент не подключен.");
            return false;
        }

        try
        {
            string command;
            if (serverConfig.Username == "root")
            {
                command = "chmod +x /root/MainInstall.sh && bash /root/MainInstall.sh";
            }
            else
            {
                command = $"chmod +x ~/MainInstall.sh && sudo su - -c \"bash /home/{serverConfig.Username}/MainInstall.sh\"";
            }

            SshCommand sshCommand = _sshClient.RunCommand(command);
            Console.WriteLine(sshCommand.Result);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Выполняет команду с правами администратора (sudo) на сервере.
    /// Автоматически подставляет пароль пользователя при необходимости (если вход выполнен не под root).
    /// </summary>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <param name="command">Выполняемая команда.</param>
    /// <returns>Объект <see cref="SshCommand"/> с результатом выполнения команды.</returns>
    /// <exception cref="InvalidOperationException">Бросается, если клиент не подключен.</exception>
    public SshCommand RunSudoCommand(ServerConfig serverConfig, string command)
    {
        if (_sshClient == null || !_sshClient.IsConnected)
        {
            throw new InvalidOperationException("SSH-клиент не подключен.");
        }

        string sudoCommand;
        if (serverConfig.Username.Equals("root", StringComparison.OrdinalIgnoreCase))
        {
            sudoCommand = command;
        }
        else
        {
            sudoCommand = $"echo '{serverConfig.Password}' | sudo -S {command}";
        }

        return _sshClient.RunCommand(sudoCommand);
    }

    /// <summary>
    /// Освобождает ресурсы, закрывая SSH и SFTP подключения.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_sshClient != null)
        {
            try
            {
                if (_sshClient.IsConnected)
                {
                    _sshClient.Disconnect();
                }
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                _sshClient.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
        if (_sftpClient != null)
        {
            try
            {
                if (_sftpClient.IsConnected)
                {
                    _sftpClient.Disconnect();
                }
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                _sftpClient.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}