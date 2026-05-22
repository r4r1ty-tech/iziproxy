using System.Collections.Generic;

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
    static void Main(string[] args)
    {
        // Создаем SSH/SFTP клиент
        using SSH ssh = new SSH();

        // Запрашиваем конфигурацию сервера у пользователя
        ServerConfig serverConfig = new ServerConfig();
        serverConfig.SetServer();

        // 1. Проверка соединения
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

        // 2. Загрузка скрипта предварительной подготовки
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

        // 3. Запуск скрипта подготовки (установка ufw, jq, curl, xray и включение BBR)
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

        // 4. Генерация ключей и параметров Xray
        XrayConfigParams xrayParams;
        try
        {
            xrayParams = XrayConfigParams.Generate(ssh, serverConfig);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка генерации ключей Xray: " + ex.Message);
            return;
        }

        // 5. Развертывание конфигурации и запуск службы на VDS
        var deployer = new DeployScripts();
        bool deployResult = deployer.DeployAndConfigure(ssh, serverConfig, xrayParams);
        if (deployResult == false)
        {
            Console.WriteLine("Ошибка деплоя конфигурации.");
            return;
        }

        // 6. Получение информации о местоположении VDS
        try
        {
            string geo = XrayConfigParams.GetGeoVDS(ssh, serverConfig);
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