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
        progress?.Report("[TRACE] XrayMonitor.GetStatus вход: запрашиваем статус Xray");
        var status = new XrayStatus();

        progress?.Report("[INFO] Проверка статуса сервиса Xray");

        // 1. Статус сервиса
        progress?.Report("[DEBUG] Выполняем: systemctl is-active xray");
        var serviceResult = await sshClient.RunSudoCommand(serverConfig, "systemctl is-active xray");
        status.IsRunning = serviceResult.Result.Trim().Equals("active", StringComparison.OrdinalIgnoreCase);
        progress?.Report(status.IsRunning
            ? "[INFO] Xray: запущен ✓"
            : "[WARN] Xray: остановлен ✗");

        // 2. Проверка конфига
        progress?.Report("[INFO] Проверка конфигурации Xray через xray -test");
        var configCheck = await sshClient.RunSudoCommand(serverConfig, "/usr/local/bin/xray -test -config /usr/local/etc/xray/config.json 2>&1");
        status.ConfigCheckOutput = configCheck.Result.Trim();
        status.IsConfigValid = !status.ConfigCheckOutput.Contains("error", StringComparison.OrdinalIgnoreCase)
                               && !status.ConfigCheckOutput.Contains("failed", StringComparison.OrdinalIgnoreCase);
        progress?.Report(status.IsConfigValid
            ? "[INFO] Конфиг валиден ✓"
            : $"[ERROR] Проблема конфига: {status.ConfigCheckOutput}");

        // 3. Статистика трафика (требует api+stats в config.json)
        if (status.IsRunning)
        {
            progress?.Report("[INFO] Запрос статистики трафика через xray api statsquery");
            var statsResult = await sshClient.RunSudoCommand(serverConfig, "/usr/local/bin/xray api statsquery --server=127.0.0.1:10085 2>&1");
            status.TrafficStats = ParseTrafficStats(statsResult.Result);
            progress?.Report($"[DEBUG] Спарсено {status.TrafficStats.Count} inbound'ов со статистикой");
        }
        else
        {
            progress?.Report("[DEBUG] Сервис не запущен — статистика трафика пропущена");
        }

        return status;
    }

    /// <summary>
    /// Перезапускает сервис Xray на удалённом сервере.
    /// </summary>
    public static async Task<bool> RestartService(SSH sshClient, ServerConfig serverConfig, IProgress<string>? progress = null)
    {
        progress?.Report("[TRACE] XrayMonitor.RestartService вход: systemctl restart xray");
        progress?.Report("[INFO] Перезапуск Xray");
        var result = await sshClient.RunSudoCommand(serverConfig, "systemctl restart xray");
        bool success = string.IsNullOrWhiteSpace(result.Error);
        if (success)
        {
            progress?.Report("[INFO] Xray перезапущен ✓");
        }
        else
        {
            progress?.Report($"[ERROR] Ошибка перезапуска: {result.Error}");
        }
        return success;
    }

    /// <summary>
    /// Парсит вывод команды <c>/usr/local/bin/xray api statsquery</c>.
    /// Возвращает список статистики трафика по каждому inbound.
    /// </summary>
    /// <remarks>
    /// Формат строки вывода (реальный формат xray-core):
    /// <code>
    /// stat: {name: "inbound>>>inbound-1>>>traffic>>>uplink", value: 12345}
    /// stat: {name: "inbound>>>inbound-1>>>traffic>>>downlink", value: 67890}
    /// </code>
    /// <para>
    /// Текущий regex ожидает формат без пробелов (старая версия xray):
    /// <c>name:"inbound>>>TAG>>>traffic>>>dir" value:N</c>. Если xray обновит
    /// формат и начнёт ставить пробелы после <c>name:</c> и <c>value:</c> —
    /// парсер сломается. Задокументировано в <c>ParseTrafficStatsTests</c>.
    /// </para>
    /// </remarks>
    /// <param name="raw">Полный stdout от <c>xray api statsquery</c>.</param>
    /// <returns>Список <see cref="InboundTrafficStat"/>, отсортированный по Tag.</returns>
    public static List<InboundTrafficStat> ParseTrafficStats(string raw)
    {
        var dict = new Dictionary<string, InboundTrafficStat>();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return dict.Values.OrderBy(s => s.Tag).ToList();
        }

        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Ищем строки вида: name:"inbound>>>TAG>>>traffic>>>dir"  value:N
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
