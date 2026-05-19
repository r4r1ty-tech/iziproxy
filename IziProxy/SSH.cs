using Renci.SshNet;
namespace IziProxy;

public class SSH : IDisposable
{
    private SshClient _sshClient = null!;
    private SftpClient _sftpClient = null!;

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
            Dispose();
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
            using var file = File.OpenRead("ScriptsVDS/MainInstall.sh");
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

    public void Dispose()
    {
        if (_sshClient != null)
        {
            _sshClient.Disconnect();
            _sshClient.Dispose();
        }
        if (_sftpClient != null)
        {
            _sftpClient.Disconnect();
            _sftpClient.Dispose();
        }
    }
}