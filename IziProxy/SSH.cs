using Renci.SshNet;
namespace IziProxy;

public class SSH : IDisposable
{
    private SshClient _sshClient = null!;
    private SftpClient _sftpClient = null!;
    private bool _disposed;

    public Boolean TestConnection(ServerConfig serverConfig)
    {
        try
        {
            var connectionInfo = new ConnectionInfo(serverConfig.Host, serverConfig.Username,
                new PasswordAuthenticationMethod(serverConfig.Username, serverConfig.Password))
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            _sshClient = new SshClient(connectionInfo);
            _sshClient.Connect();

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
            if (serverConfig.Username == "root")
            {
                targetPath = "/root/MainInstall.sh";
            }
            else
            {
                targetPath = "MainInstall.sh";
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
            
            _sftpClient.UploadFile(file, targetPath, true); // true = overwrite
            Console.WriteLine($"Файл {localFilePath} загружен успешно в {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }

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