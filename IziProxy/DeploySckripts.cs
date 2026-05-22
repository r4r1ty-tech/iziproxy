using Renci.SshNet;
using System.IO;

namespace IziProxy;

/// <summary>
/// Предоставляет методы для загрузки скрипта развертывания и конфигурации на удаленный VDS и их выполнения.
/// </summary>
public class DeployScripts
{
    /// <summary>
    /// Загружает скрипты развертывания, формирует файл конфигурации config.json на основе параметров Xray,
    /// передает их на удаленный сервер и запускает процесс деплоя.
    /// </summary>
    /// <param name="sshClient">Подключенный клиент для отправки SSH-команд.</param>
    /// <param name="serverConfig">Параметры авторизации и IP-адрес сервера.</param>
    /// <param name="xrayParams">Параметры конфигурации Xray (UUID, ключи, порт, SNI).</param>
    /// <returns>True, если деплой выполнен успешно; иначе false.</returns>
    public bool DeployAndConfigure(SSH sshClient, ServerConfig serverConfig, XrayConfigParams xrayParams)
    {
        Console.WriteLine("Загрузка Deploy.sh...");
        // Загружаем скрипт Deploy.sh во временную/домашнюю директорию на сервере
        bool isDeployUploaded = sshClient.UploadFile("VDS_setup/Deploy.sh", "Deploy.sh", serverConfig);
        
        if (!isDeployUploaded)
        {
            Console.WriteLine("Не удалось загрузить Deploy.sh");
            return false;
        }

        Console.WriteLine("Формирование config.json...");
        // Читаем шаблон конфигурации Xray
        string configContent = File.ReadAllText("VDS_setup/config.json");
        // Заменяем плейсхолдеры на сгенерированные значения
        configContent = configContent.Replace("__UUID__", xrayParams.Uuid)
                                     .Replace("__PRIVATE_KEY__", xrayParams.PrivateKey)
                                     .Replace("__SHORT_ID__", xrayParams.ShortId);

        string tempConfigPath = "temp_config.json";
        File.WriteAllText(tempConfigPath, configContent);

        Console.WriteLine("Загрузка config.json...");
        // Загружаем сформированный config.json на VDS
        bool isConfigUploaded = sshClient.UploadFile(tempConfigPath, "config.json", serverConfig);
        
        // Удаляем временный локальный файл конфигурации
        if (File.Exists(tempConfigPath))
        {
            File.Delete(tempConfigPath);
        }

        if (!isConfigUploaded)
        {
            Console.WriteLine("Не удалось загрузить config.json");
            return false;
        }

        // Составляем команду запуска деплоя в зависимости от того, под каким пользователем мы авторизовались
        string runCommand;
        if (serverConfig.Username == "root")
        {
            runCommand = "chmod +x /root/Deploy.sh && bash /root/Deploy.sh";
        }
        else
        {
            runCommand = $"chmod +x ~/Deploy.sh && sudo su - -c \"bash /home/{serverConfig.Username}/Deploy.sh\"";
        }

        Console.WriteLine("Выполнение Deploy.sh на сервере...");
        var result = sshClient.RunSudoCommand(serverConfig, runCommand);
        string output = result.Result;
        Console.WriteLine(output);

        // Парсим вывод скрипта, чтобы получить реально выбранные свободный порт и лучший домен для SNI
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("SELECTED_PORT="))
            {
                xrayParams.Port = line.Substring("SELECTED_PORT=".Length).Trim();
            }
            else if (line.StartsWith("SNI_SELECTED="))
            {
                xrayParams.Sni = line.Substring("SNI_SELECTED=".Length).Trim();
            }
        }

        return true;
    }
}
