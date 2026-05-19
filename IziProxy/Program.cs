using Renci.SshNet;
namespace IziProxy;

class Program
{
    static void Main(string[] args)
    {
        using SSH ssh = new SSH();
        ServerConfig serverConfig = new ServerConfig();
        serverConfig.SetServer();

        bool resultTestConnection = ssh.TestConnection(serverConfig);
        if (resultTestConnection == true)
        {
            Console.WriteLine("Подключение успешно установлено");
        }
        else
        {
            Console.WriteLine("Ошибка подключения. Выход из программы...");
            return;
        }

        bool resultTestUpload = ssh.UploadTestScript(serverConfig);
        if (resultTestUpload == true)
        {
            Console.WriteLine("Файл успешно загружен");
        }
        else
        {
            Console.WriteLine("Ошибка загрузки файла. Выход из программы...");
            return;
        }

        bool resultRunTestScript = ssh.RunTestScript(serverConfig);
        if (resultRunTestScript == true)
        {
            Console.WriteLine("Скрипт успешно выполнен");
        }
        else
        {
            Console.WriteLine("Ошибка при выполнении скрипта.");
        }
    }
}