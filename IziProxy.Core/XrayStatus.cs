using Renci.SshNet;

namespace IziProxy;

/// <summary>
/// Результат проверки состояния Xray-сервиса и статистики трафика.
/// </summary>
public class XrayStatus
{
    /// <summary>Xray сервис запущен (active/running).</summary>
    public bool IsRunning { get; set; }

    /// <summary>Вывод команды проверки конфигурации.</summary>
    public string ConfigCheckOutput { get; set; } = string.Empty;

    /// <summary>True если конфиг валиден.</summary>
    public bool IsConfigValid { get; set; }

    /// <summary>Статистика по inbound'ам (tag → uplink/downlink в байтах).</summary>
    public List<InboundTrafficStat> TrafficStats { get; set; } = new();
}

/// <summary>
/// Статистика трафика одного inbound'а.
/// </summary>
public class InboundTrafficStat
{
    public string Tag { get; set; } = string.Empty;
    public long UplinkBytes { get; set; }
    public long DownlinkBytes { get; set; }

    public string UplinkFormatted => FormatBytes(UplinkBytes);
    public string DownlinkFormatted => FormatBytes(DownlinkBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F2} MB";
        if (bytes >= 1024)          return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }
}
