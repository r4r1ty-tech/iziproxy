using Renci.SshNet;
using System.IO;

namespace IziProxy;

/// <summary>
/// Результат парсинга <c>Deploy.sh</c>: три порта и три SNI для трёх inbound'ов Xray.
/// </summary>
/// <param name="Port1">Port для inbound-1 (443 или рандомный).</param>
/// <param name="Port2">Port для inbound-2 (8443 или рандомный).</param>
/// <param name="Port3">Port для inbound-3 (рандомный).</param>
/// <param name="Sni1">SNI домен для inbound-1.</param>
/// <param name="Sni2">SNI домен для inbound-2.</param>
/// <param name="Sni3">SNI домен для inbound-3.</param>
public record DeployResult(
    string Port1, string Port2, string Port3,
    string Sni1, string Sni2, string Sni3);

/// <summary>
/// Предоставляет методы для загрузки скрипта развертывания и конфигурации на удаленный VDS и их выполнения.
/// </summary>
public class DeployScripts
{
    /// <summary>
    /// Парсит вывод скрипта <c>Deploy.sh</c> и возвращает три порта и три SNI.
    /// </summary>
    /// <remarks>
    /// <para>Скрипт выводит по одной строке на параметр:</para>
    /// <code>
    /// SELECTED_PORT_1=443
    /// SELECTED_PORT_2=8443
    /// SELECTED_PORT_3=50001
    /// SNI_SELECTED_1=cdn.example.com
    /// SNI_SELECTED_2=static.example.com
    /// SNI_SELECTED_3=speed.example.com
    /// </code>
    /// <para>
    /// Если какого-то параметра нет — соответствующее поле <see cref="DeployResult"/>
    /// остаётся пустой строкой. Валидация (что все 6 непустые) делается
    /// caller'ом, не здесь — это позволяет тестировать парсинг без
    /// обязательной валидации.
    /// </para>
    /// </remarks>
    /// <param name="raw">Полный stdout скрипта Deploy.sh.</param>
    /// <returns><see cref="DeployResult"/> с шестью полями (пустые если не нашлись).</returns>
    public static DeployResult ParseDeployOutput(string raw)
    {
        string port1 = "", port2 = "", port3 = "";
        string sni1 = "", sni2 = "", sni3 = "";

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new DeployResult(port1, port2, port3, sni1, sni2, sni3);
        }

        foreach (var line in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("SELECTED_PORT_1=", StringComparison.Ordinal))
                port1 = line["SELECTED_PORT_1=".Length..].Trim();
            else if (line.StartsWith("SELECTED_PORT_2=", StringComparison.Ordinal))
                port2 = line["SELECTED_PORT_2=".Length..].Trim();
            else if (line.StartsWith("SELECTED_PORT_3=", StringComparison.Ordinal))
                port3 = line["SELECTED_PORT_3=".Length..].Trim();
            else if (line.StartsWith("SNI_SELECTED_1=", StringComparison.Ordinal))
                sni1 = line["SNI_SELECTED_1=".Length..].Trim();
            else if (line.StartsWith("SNI_SELECTED_2=", StringComparison.Ordinal))
                sni2 = line["SNI_SELECTED_2=".Length..].Trim();
            else if (line.StartsWith("SNI_SELECTED_3=", StringComparison.Ordinal))
                sni3 = line["SNI_SELECTED_3=".Length..].Trim();
        }

        return new DeployResult(port1, port2, port3, sni1, sni2, sni3);
    }

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
        bool isDeployUploaded = await sshClient.UploadFile(EmbeddedScripts.OpenDeploy(), "Deploy.sh", serverConfig, progress);

        if (!isDeployUploaded)
        {
            progress?.Report("Не удалось загрузить Deploy.sh");
            return false;
        }

        progress?.Report("Формирование config.json...");
        string configContent = await Task.Run(() => EmbeddedScripts.ReadConfigJson());

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

        string homeDir = serverConfig.Username == "root" ? "/root" : $"/home/{serverConfig.Username}";
        string runCommand = $"chmod +x {homeDir}/Deploy.sh && bash {homeDir}/Deploy.sh";

        progress?.Report($"[DEBUG] Выполнение Deploy.sh на сервере: {runCommand}");
        var result = await sshClient.RunSudoCommand(serverConfig, runCommand);
        string output = result.Result;
        progress?.Report($"[DEBUG] Вывод Deploy.sh:\n{output}");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            progress?.Report($"[DEBUG] Ошибки Deploy.sh (stderr):\n{result.Error}");
        }

        // Парсим порты и SNI из вывода скрипта через ParseDeployOutput.
        DeployResult deployResult = ParseDeployOutput(output);
        string port1 = deployResult.Port1;
        string port2 = deployResult.Port2;
        string port3 = deployResult.Port3;
        string sni1 = deployResult.Sni1;
        string sni2 = deployResult.Sni2;
        string sni3 = deployResult.Sni3;

        progress?.Report($"[DEBUG] Спаршены порты: {port1}, {port2}, {port3}");
        progress?.Report($"[DEBUG] Спаршены SNI: {sni1}, {sni2}, {sni3}");

        if (string.IsNullOrWhiteSpace(port1) || string.IsNullOrWhiteSpace(port2) || string.IsNullOrWhiteSpace(port3))
        {
            throw new Exception("Критическая ошибка: скрипт Deploy.sh не вернул порты (или вернул пустые).");
        }

        if (string.IsNullOrWhiteSpace(sni1) || string.IsNullOrWhiteSpace(sni2) || string.IsNullOrWhiteSpace(sni3))
        {
            throw new Exception("Критическая ошибка: скрипт Deploy.sh не вернул SNI (или вернул пустые).");
        }

        xrayParams.Ports.Add(port1);
        xrayParams.Ports.Add(port2);
        xrayParams.Ports.Add(port3);

        xrayParams.Snis.Add(sni1);
        xrayParams.Snis.Add(sni2);
        xrayParams.Snis.Add(sni3);

        progress?.Report("Применение конфигурации Xray...");
        string xrayConfDir = "/usr/local/etc/xray";
        string targetConfPath = $"{xrayConfDir}/config.json";
        
        string configSource = serverConfig.Username == "root" ? "/root/config.json" : $"/home/{serverConfig.Username}/config.json";
        
        string applyCommand = $@"
mkdir -p {xrayConfDir} && \
cp {configSource} {targetConfPath} && \
systemctl restart xray && \
sleep 2 && \
if systemctl is-active --quiet xray; then
  echo ""XRAY_STATUS=ok""
else
  echo ""XRAY_STATUS=failed""
  journalctl -u xray -n 15 --no-pager --output=cat
fi
";

        progress?.Report($"[DEBUG] Копирование конфига и рестарт (с проверкой статуса)...");
        var applyResult = await sshClient.RunSudoCommand(serverConfig, applyCommand);
        string applyOutput = applyResult.Result ?? "";
        
        progress?.Report($"[DEBUG] Результат рестарта:\n{applyOutput}");
        if (!string.IsNullOrWhiteSpace(applyResult.Error))
        {
            progress?.Report($"[DEBUG] Ошибки рестарта (stderr):\n{applyResult.Error}");
        }

        if (applyOutput.Contains("XRAY_STATUS=failed"))
        {
            int errorIndex = applyOutput.IndexOf("XRAY_STATUS=failed") + "XRAY_STATUS=failed".Length;
            string errorLog = applyOutput.Substring(errorIndex).Trim();
            throw new Exception($"Xray упал после рестарта (ошибка конфигурации или портов):\n{errorLog}");
        }

        progress?.Report("Сервис Xray успешно перезапущен и активен.");

        return true;
    }
}
