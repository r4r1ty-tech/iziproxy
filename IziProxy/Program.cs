namespace IziProxy;

/// <summary>
/// Главный класс программы, координирующий весь процесс развертывания прокси-сервера.
/// </summary>
class Program
{
    /// <summary>
    /// Точка входа в приложение. Выполняет шаги подключения, установки зависимостей,
    /// генерации ключей, деплоя конфигурации и вывода ссылок для клиента.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    static async Task Main(string[] args)
    {
        // Все лог-сообщения из Core пишутся прямо в консоль
        var progress = new Progress<string>(Console.WriteLine);

        // Создаем SSH/SFTP клиент
        using SSH ssh = new SSH();

        // Запрашиваем конфигурацию сервера у пользователя
        ServerConfig serverConfig = new ServerConfig();
        serverConfig.SetServer();

        // 1. Проверка соединения
        bool resultTestConnection = await ssh.TestConnection(serverConfig, progress);
        if (resultTestConnection)
        {
            Console.WriteLine("Подключение успешно установлено");
        }
        else
        {
            Console.WriteLine("Ошибка подключения. Выход из программы...");
            return;
        }

        // 2. Загрузка скрипта предварительной подготовки
        Console.WriteLine("Загрузка установочного скрипта...");
        bool resultTestUpload = await ssh.UploadTestScript(serverConfig, progress);
        if (resultTestUpload)
        {
            Console.WriteLine("Файл успешно загружен");
        }
        else
        {
            Console.WriteLine("Ошибка загрузки файла. Выход из программы...");
            return;
        }

        // 3. Запуск скрипта подготовки (установка ufw, jq, curl, xray и включение BBR)
        Console.WriteLine("Запуск установочного скрипта...");
        bool resultRunTestScript = await ssh.RunTestScript(serverConfig, progress);
        if (resultRunTestScript)
        {
            Console.WriteLine("Скрипт успешно выполнен");
        }
        else
        {
            Console.WriteLine("Ошибка при выполнении скрипта.");
            return;
        }

        // 4. Генерация ключей и параметров Xray
        XrayConfigParams xrayParams;
        try
        {
            xrayParams = await XrayConfigParams.Generate(ssh, serverConfig, progress);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка генерации ключей Xray: " + ex.Message);
            return;
        }

        // 5. Развертывание конфигурации и запуск службы на VDS
        var deployer = new DeployScripts();
        bool deployResult = await deployer.DeployAndConfigure(ssh, serverConfig, xrayParams, progress);
        if (!deployResult)
        {
            Console.WriteLine("Ошибка деплоя конфигурации.");
            return;
        }

        // 6. Получение информации о местоположении VDS
        try
        {
            string geo = await XrayConfigParams.GetGeoVDS(ssh, serverConfig, progress);
            Console.WriteLine("GEO VDS: " + geo);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка получения GEO: " + ex.Message);
        }

        // 7. Генерация и вывод VLESS ссылок для всех 3 inbound'ов
        List<string> vlessLinks = VlessLinkGenerator.GenerateRealityLinks(serverConfig, xrayParams);

        Console.WriteLine("");
        Console.WriteLine("=== VLESS ссылки ===");

        for (int i = 0; i < vlessLinks.Count; i++)
        {
            string port = xrayParams.Ports[i];
            string sni = xrayParams.Snis[i];

            Console.WriteLine("");
            Console.WriteLine("Ссылка " + (i + 1) + " | Порт: " + port + " | SNI: " + sni);
            Console.WriteLine(vlessLinks[i]);
        }

        Console.WriteLine("");
        Console.WriteLine("=== Готово ===");
    }
}