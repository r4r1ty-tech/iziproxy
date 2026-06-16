using System;
using System.Collections.Generic;
using Xunit;

namespace IziProxy.Tests;

/// <summary>
/// Тесты для <see cref="XrayMonitor.ParseTrafficStats"/> (F-06 из FutureTest.md).
/// Покрывают: happy path с одним inbound, несколько inbound'ов, пустой
/// ввод, битый формат, длинные числа, агрегацию uplink+downlink.
/// </summary>
public class ParseTrafficStatsTests
{
    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyList()
    {
        var stats = XrayMonitor.ParseTrafficStats("");

        Assert.Empty(stats);
    }

    [Fact]
    public void Parse_NullInput_ReturnsEmptyList()
    {
        var stats = XrayMonitor.ParseTrafficStats(null!);

        Assert.Empty(stats);
    }

    [Fact]
    public void Parse_OnlyWhitespace_ReturnsEmptyList()
    {
        var stats = XrayMonitor.ParseTrafficStats("   \n\n   \n");

        Assert.Empty(stats);
    }

    [Fact]
    public void Parse_SingleInbound_UplinkAndDownlink_Aggregated()
    {
        // Формат без пробелов (старый xray) — текущий парсер это поддерживает.
        const string output = """
            stat:{name:"inbound>>>inbound-1>>>traffic>>>uplink" value:12345}
            stat:{name:"inbound>>>inbound-1>>>traffic>>>downlink" value:67890}
            """;

        var stats = XrayMonitor.ParseTrafficStats(output);

        Assert.Single(stats);
        Assert.Equal("inbound-1", stats[0].Tag);
        Assert.Equal(12345L, stats[0].UplinkBytes);
        Assert.Equal(67890L, stats[0].DownlinkBytes);
    }

    [Fact]
    public void Parse_MultipleInbounds_AllReturnedSortedByTag()
    {
        // 3 inbound'а в разном порядке в выводе — результат сортируется по Tag.
        const string output = """
            stat:{name:"inbound>>>inbound-2>>>traffic>>>uplink" value:200}
            stat:{name:"inbound>>>inbound-1>>>traffic>>>uplink" value:100}
            stat:{name:"inbound>>>inbound-3>>>traffic>>>uplink" value:300}
            stat:{name:"inbound>>>inbound-1>>>traffic>>>downlink" value:10}
            """;

        var stats = XrayMonitor.ParseTrafficStats(output);

        Assert.Equal(3, stats.Count);
        Assert.Equal("inbound-1", stats[0].Tag);
        Assert.Equal(100L, stats[0].UplinkBytes);
        Assert.Equal(10L, stats[0].DownlinkBytes);

        Assert.Equal("inbound-2", stats[1].Tag);
        Assert.Equal(200L, stats[1].UplinkBytes);
        Assert.Equal(0L, stats[1].DownlinkBytes);

        Assert.Equal("inbound-3", stats[2].Tag);
        Assert.Equal(300L, stats[2].UplinkBytes);
    }

    [Fact]
    public void Parse_RealXrayFormatWithSpaces_GapDocumented()
    {
        // GAP-АНАЛИЗ: реальный формат xray-core statsquery использует
        // пробелы после 'name:' и 'value:' (stat: {name: "...", value: 12345}).
        // Текущий regex ищет ровно name:"..." без пробела — НЕ сматчит.
        // Этот тест ЗАКРЕПЛЯЕТ текущее поведение: пробелы = строки
        // пропускаются молча.
        const string output = """
            stat: {name: "inbound>>>inbound-1>>>traffic>>>uplink", value: 12345}
            stat: {name: "inbound>>>inbound-1>>>traffic>>>downlink", value: 67890}
            """;

        var stats = XrayMonitor.ParseTrafficStats(output);

        // Реально: текущий regex не сматчит формат с пробелами → 0 inbound'ов
        Assert.Empty(stats);
    }

    [Fact]
    public void Parse_ZeroValues_AreKeptAsZero()
    {
        const string output = """
            stat:{name:"inbound>>>i>>>traffic>>>uplink" value:0}
            stat:{name:"inbound>>>i>>>traffic>>>downlink" value:0}
            """;

        var stats = XrayMonitor.ParseTrafficStats(output);

        Assert.Single(stats);
        Assert.Equal(0L, stats[0].UplinkBytes);
        Assert.Equal(0L, stats[0].DownlinkBytes);
    }

    [Fact]
    public void Parse_LongValues_AreParsedAsLong()
    {
        // xray может накапливать очень большие числа. 10 GB = 10_737_418_240.
        // 1 TB = ~1.1e12 — long.MaxValue = 9.2e18, так что должно влезать.
        const long OneTb = 1_099_511_627_776L;
        string output = $$"""
            stat:{name:"inbound>>>i>>>traffic>>>uplink" value:{{OneTb}}}
            stat:{name:"inbound>>>i>>>traffic>>>downlink" value:0}
            """;

        var stats = XrayMonitor.ParseTrafficStats(output);

        Assert.Single(stats);
        Assert.Equal(OneTb, stats[0].UplinkBytes);
    }

    [Fact]
    public void Parse_MalformedLine_SilentlySkipped()
    {
        // Битый JSON-like формат: строка есть, но без нужных полей.
        // Парсер не должен падать, просто пропускает.
        const string output = """
            stat:{name:"inbound>>>i>>>traffic>>>uplink" value:100}
            this is random garbage that should be ignored
            stat:{not_a_valid_name value:200}
            stat:{name:"inbound>>>i>>>traffic>>>downlink" value:50}
            """;

        var stats = XrayMonitor.ParseTrafficStats(output);

        Assert.Single(stats);
        Assert.Equal(100L, stats[0].UplinkBytes);
        Assert.Equal(50L, stats[0].DownlinkBytes);
    }

    [Fact]
    public void Parse_UplinkOnlyDownlinkMissing_DownlinkIsZero()
    {
        // Inbound виден в uplink, но не в downlink (новый inbound, ещё нет
        // трафика вниз) — DownlinkBytes должен быть 0, не -1 и не missing.
        const string output = """
            stat:{name:"inbound>>>i>>>traffic>>>uplink" value:42}
            """;

        var stats = XrayMonitor.ParseTrafficStats(output);

        Assert.Single(stats);
        Assert.Equal(42L, stats[0].UplinkBytes);
        Assert.Equal(0L, stats[0].DownlinkBytes);
    }

    [Fact]
    public void Parse_InvalidValue_FallsBackToZero()
    {
        // value:abc — не парсится как long. Текущая реализация
        // использует long.TryParse(..., out var v) ? v : 0 → fallback в 0.
        const string output = """
            stat:{name:"inbound>>>i>>>traffic>>>uplink" value:not_a_number}
            stat:{name:"inbound>>>i>>>traffic>>>downlink" value:50}
            """;

        var stats = XrayMonitor.ParseTrafficStats(output);

        Assert.Single(stats);
        Assert.Equal(0L, stats[0].UplinkBytes);
        Assert.Equal(50L, stats[0].DownlinkBytes);
    }

    [Fact]
    public void Parse_NonInboundStats_AreIgnored()
    {
        // xray может возвращать и другие типы статистики: user>>>email>>>traffic,
        // inbound>>>...>>>connection, и т.д. Парсер должен игнорировать всё
        // кроме inbound>>>...>>>traffic.
        const string output = """
            stat:{name:"user>>>user1@example.com>>>traffic>>>uplink" value:999}
            stat:{name:"inbound>>>inbound-1>>>traffic>>>uplink" value:100}
            stat:{name:"inbound>>>inbound-1>>>connection" value:5}
            stat:{name:"inbound>>>inbound-1>>>traffic>>>downlink" value:50}
            """;

        var stats = XrayMonitor.ParseTrafficStats(output);

        Assert.Single(stats);
        Assert.Equal("inbound-1", stats[0].Tag);
        Assert.Equal(100L, stats[0].UplinkBytes);
        Assert.Equal(50L, stats[0].DownlinkBytes);
    }
}
