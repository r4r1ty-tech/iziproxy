using Renci.SshNet;
using System.IO;

namespace IziProxy;

public class DeployScripts
{
    public bool DeployAndConfigure(SSH sshClient, ServerConfig serverConfig, XrayConfigParams xrayParams)
    {
        Console.WriteLine("Загрузка Deploy.sh...");
        bool isDeployUploaded = sshClient.UploadFile("VDS_setup/Deploy.sh", "Deploy.sh", serverConfig);
        
        if (!isDeployUploaded)
        {
            Console.WriteLine("Не удалось загрузить Deploy.sh");
            return false;
        }

        Console.WriteLine("Формирование config.json...");
        string configContent = File.ReadAllText("VDS_setup/config.json");
        configContent = configContent.Replace("__UUID__", xrayParams.Uuid)
                                     .Replace("__PRIVATE_KEY__", xrayParams.PrivateKey)
                                     .Replace("__SHORT_ID__", xrayParams.ShortId);

        string tempConfigPath = "temp_config.json";
        File.WriteAllText(tempConfigPath, configContent);

        Console.WriteLine("Загрузка config.json...");
        bool isConfigUploaded = sshClient.UploadFile(tempConfigPath, "config.json", serverConfig);
        
        if (File.Exists(tempConfigPath))
        {
            File.Delete(tempConfigPath);
        }

        if (!isConfigUploaded)
        {
            Console.WriteLine("Не удалось загрузить config.json");
            return false;
        }

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

        // Пытаемся вытащить порт, который скрипт реально назначил
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
