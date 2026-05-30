using Renci.SshNet;

namespace IziProxy;

/// <summary>
/// Методы для мониторинга состояния Xray-сервиса на удалённом сервере.
/// </summary>
public class XrayMonitor
{
    /// <summary>
    /// Получает статус сервиса Xray, проверяет валидность конфига и собирает статистику трафика.
    /// </summary>
    /// <param name="sshClient">Подключённый SSH-клиент.</param>
    /// <param name="serverConfig">Конфигурация сервера.</param>
    /// <param name="progress">Получатель прогресса.</param>
    public static async Task<XrayStatus> GetStatus(SSH sshClient, ServerConfig serverConfig, IProgress<string>? progress = null)
    {
        var status = new XrayStatus();

        progress?.Report("Проверка статуса сервиса Xray...");

        // 1. Статус сервиса
        var serviceResult = await sshClient.RunSudoCommand(serverConfig, "systemctl is-active xray");
        status.IsRunning = serviceResult.Result.Trim().Equals("active", StringComparison.OrdinalIgnoreCase);
        progress?.Report(status.IsRunning ? "Xray: запущен ✓" : "Xray: остановлен ✗");

        // 2. Проверка конфига
        progress?.Report("Проверка конфигурации Xray...");
        var configCheck = await sshClient.RunSudoCommand(serverConfig, "/usr/local/bin/xray -test -config /usr/local/etc/xray/config.json 2>&1");
        status.ConfigCheckOutput = configCheck.Result.Trim();
        status.IsConfigValid = !status.ConfigCheckOutput.Contains("error", StringComparison.OrdinalIgnoreCase)
                               && !status.ConfigCheckOutput.Contains("failed", StringComparison.OrdinalIgnoreCase);
        progress?.Report(status.IsConfigValid ? "Конфиг валиден ✓" : $"Проблема конфига: {status.ConfigCheckOutput}");

        // 3. Статистика трафика (требует api+stats в config.json)
        if (status.IsRunning)
        {
            progress?.Report("Запрос статистики трафика...");
            var statsResult = await sshClient.RunSudoCommand(serverConfig, "/usr/local/bin/xray api statsquery --server=127.0.0.1:10085 2>&1");
            status.TrafficStats = ParseTrafficStats(statsResult.Result);
        }

        return status;
    }

    /// <summary>
    /// Перезапускает сервис Xray на удалённом сервере.
    /// </summary>
    public static async Task<bool> RestartService(SSH sshClient, ServerConfig serverConfig, IProgress<string>? progress = null)
    {
        progress?.Report("Перезапуск Xray...");
        var result = await sshClient.RunSudoCommand(serverConfig, "systemctl restart xray");
        bool success = string.IsNullOrWhiteSpace(result.Error);
        progress?.Report(success ? "Xray перезапущен ✓" : $"Ошибка: {result.Error}");
        return success;
    }

    /// <summary>
    /// Парсит вывод команды xray api statsquery.
    /// Пример строк вывода:
    ///   stat:{name:"inbound>>>inbound-1>>>traffic>>>uplink" value:12345}
    /// </summary>
    private static List<InboundTrafficStat> ParseTrafficStats(string raw)
    {
        var dict = new Dictionary<string, InboundTrafficStat>();

        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // ищем строки вида: name:"inbound>>>TAG>>>traffic>>>uplink"  value:12345
            var nameMatch = System.Text.RegularExpressions.Regex.Match(line, @"name:""inbound>>>([^>]+)>>>traffic>>>(uplink|downlink)""");
            var valueMatch = System.Text.RegularExpressions.Regex.Match(line, @"value:(\d+)");

            if (!nameMatch.Success || !valueMatch.Success) continue;

            string tag = nameMatch.Groups[1].Value;
            string direction = nameMatch.Groups[2].Value;
            long bytes = long.TryParse(valueMatch.Groups[1].Value, out var v) ? v : 0;

            if (!dict.TryGetValue(tag, out var stat))
            {
                stat = new InboundTrafficStat { Tag = tag };
                dict[tag] = stat;
            }

            if (direction == "uplink")   stat.UplinkBytes   = bytes;
            if (direction == "downlink") stat.DownlinkBytes = bytes;
        }

        return dict.Values.OrderBy(s => s.Tag).ToList();
    }
}
