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

        Console.WriteLine("Загрузка установочного скрипта...");
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

        Console.WriteLine("Запуск установочного скрипта...");
        bool resultRunTestScript = ssh.RunTestScript(serverConfig);
        if (resultRunTestScript == true)
        {
            Console.WriteLine("Скрипт успешно выполнен");
        }
        else
        {
            Console.WriteLine("Ошибка при выполнении скрипта.");
            return;
        }

        XrayConfigParams xrayParams;
        try
        {
            xrayParams = XrayConfigParams.Generate(ssh, serverConfig);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка генерации ключей Xray: {ex.Message}");
            return;
        }

        var deployer = new DeployScripts();
        bool deployResult = deployer.DeployAndConfigure(ssh, serverConfig, xrayParams);
        if (!deployResult)
        {
            Console.WriteLine("Ошибка деплоя конфигурации.");
            return;
        }

        try
        {
            string geo = XrayConfigParams.GetGeoVDS(ssh, serverConfig);
            Console.WriteLine($"GEO VDS: {geo}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка получения GEO: {ex.Message}");
        }

        string vlessLink = VlessLinkGenerator.GenerateRealityLink(serverConfig, xrayParams);
        Console.WriteLine("VLESS ссылка:");
        Console.WriteLine(vlessLink);
    }
}