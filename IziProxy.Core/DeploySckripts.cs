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
    /// После успешного деплоя заполняет xrayParams.Ports и xrayParams.Snis из вывода скрипта.
    /// </summary>
    /// <param name="sshClient">Подключенный клиент для отправки SSH-команд.</param>
    /// <param name="serverConfig">Параметры авторизации и IP-адрес сервера.</param>
    /// <param name="xrayParams">Параметры конфигурации Xray (UUID, ключи, порт, SNI).</param>
    /// <param name="progress">Получатель прогресса и сообщений об ошибках.</param>
    /// <returns>True, если деплой выполнен успешно; иначе false.</returns>
    public async Task<bool> DeployAndConfigure(SSH sshClient, ServerConfig serverConfig, XrayConfigParams xrayParams, IProgress<string>? progress = null)
    {
        progress?.Report("Загрузка Deploy.sh...");
        bool isDeployUploaded = await sshClient.UploadFile(Path.Combine(AppContext.BaseDirectory, "VDS_setup", "Deploy.sh"), "Deploy.sh", serverConfig, progress);

        if (!isDeployUploaded)
        {
            progress?.Report("Не удалось загрузить Deploy.sh");
            return false;
        }

        progress?.Report("Формирование config.json...");
        string configContent = await Task.Run(() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "VDS_setup", "config.json")));

        configContent = configContent.Replace("__UUID__", xrayParams.Uuid)
                                     .Replace("__PRIVATE_KEY__", xrayParams.PrivateKey)
                                     .Replace("__SHORT_ID__", xrayParams.ShortId);

        string tempConfigPath = Path.Combine(Path.GetTempPath(), "iziproxy_temp_config.json");
        await Task.Run(() => File.WriteAllText(tempConfigPath, configContent));

        progress?.Report("Загрузка config.json...");
        bool isConfigUploaded = await sshClient.UploadFile(tempConfigPath, "config.json", serverConfig, progress);

        await Task.Run(() =>
        {
            if (File.Exists(tempConfigPath))
                File.Delete(tempConfigPath);
        });

        if (!isConfigUploaded)
        {
            progress?.Report("Не удалось загрузить config.json");
            return false;
        }

        string runCommand;
        if (serverConfig.Username == "root")
            runCommand = "chmod +x /root/Deploy.sh && bash /root/Deploy.sh";
        else
            runCommand = $"chmod +x ~/Deploy.sh && sudo su - -c \"bash /home/{serverConfig.Username}/Deploy.sh\"";

        progress?.Report("Выполнение Deploy.sh на сервере...");
        var result = await sshClient.RunSudoCommand(serverConfig, runCommand);
        string output = result.Result;
        progress?.Report(output);

        // Парсим порты и SNI из вывода скрипта.
        // Скрипт выводит строки вида:
        //   SELECTED_PORT_1=443
        //   SELECTED_PORT_2=8443
        //   SELECTED_PORT_3=31337
        //   SNI_SELECTED_1=speed.cloudflare.com
        //   SNI_SELECTED_2=cdn.jsdelivr.net
        //   SNI_SELECTED_3=www.microsoft.com

        string port1 = string.Empty;
        string port2 = string.Empty;
        string port3 = string.Empty;
        string sni1 = string.Empty;
        string sni2 = string.Empty;
        string sni3 = string.Empty;

        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("SELECTED_PORT_1="))
                port1 = line.Substring("SELECTED_PORT_1=".Length).Trim();
            else if (line.StartsWith("SELECTED_PORT_2="))
                port2 = line.Substring("SELECTED_PORT_2=".Length).Trim();
            else if (line.StartsWith("SELECTED_PORT_3="))
                port3 = line.Substring("SELECTED_PORT_3=".Length).Trim();
            else if (line.StartsWith("SNI_SELECTED_1="))
                sni1 = line.Substring("SNI_SELECTED_1=".Length).Trim();
            else if (line.StartsWith("SNI_SELECTED_2="))
                sni2 = line.Substring("SNI_SELECTED_2=".Length).Trim();
            else if (line.StartsWith("SNI_SELECTED_3="))
                sni3 = line.Substring("SNI_SELECTED_3=".Length).Trim();
        }

        xrayParams.Ports.Add(port1);
        xrayParams.Ports.Add(port2);
        xrayParams.Ports.Add(port3);

        xrayParams.Snis.Add(sni1);
        xrayParams.Snis.Add(sni2);
        xrayParams.Snis.Add(sni3);

        return true;
    }
}
