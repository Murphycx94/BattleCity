using System.Globalization;
using Tank.Client.Match;
using Tank.Net.Battle;

namespace Tank.Art.Tests;

public class NetPanelTextTests
{
    private static readonly BattleStats Stats = new(
        RttMs: 38, JitterMs: 3.4, InterpDelayMs: 100, PacketLossPercent: 0f, BytesInPerSecond: 7168, BytesOutPerSecond: 1536,
        SnapshotsPerSecond: 30, SnapshotBytes: 236, Extrapolations: 0, Corrections: 14, LastCorrection: 0.031f, MeanCorrection: 0.012,
        MaxCorrection: 0.43f, InputSlackTicks: 2.1, ClockRate: 1.002, DroppedPackets: 3);

    private static string[] Fill(in NetSample sample, out int count)
    {
        var lines = new string[NetPanelText.MaxLines];
        count = NetPanelText.Lines(sample, lines);
        return lines;
    }

    private static string Joined(string[] lines, int count) => string.Join("\n", lines.Take(count));

    [Fact]
    public void The_lines_carry_reconciliation_snapshot_and_traffic_figures()
    {
        string[] lines = Fill(new NetSample(Stats, Resyncs: 1, HasServerTick: false, 0, 0, Fps: 144), out int count);
        string text = Joined(lines, count);

        Assert.Contains("延迟 38 ms", text);
        Assert.Contains("抖动 3.4 ms", text);
        Assert.Contains("插值 100 ms", text);
        Assert.Contains("快照 30/s · 236 B", text);
        Assert.Contains("下行 7.0 KB/s", text);
        Assert.Contains("上行 1.5 KB/s", text);
        Assert.Contains("时钟 ×1.002", text);
        Assert.Contains("余量 2.1 tick", text);
        Assert.Contains("对账 14 次", text);
        Assert.Contains("最近 0.031 m", text);
        Assert.Contains("平均 0.012 m", text);
        Assert.Contains("最大 0.430 m", text);
        Assert.Contains("丢包 3", text);
        Assert.Contains("重同步 1", text);
        Assert.Contains("144 fps", text);
    }

    [Fact]
    public void The_server_tick_line_shows_only_when_this_process_hosts()
    {
        Fill(new NetSample(Stats, 0, HasServerTick: false, 0, 0, 60), out int without);
        string[] lines = Fill(new NetSample(Stats, 0, HasServerTick: true, 0.4, 2.1, 60), out int with);

        Assert.Equal(without + 1, with);
        Assert.Contains("服务器 tick 平均 0.4 ms · 最大 2.1 ms", Joined(lines, with));
        Assert.True(with <= NetPanelText.MaxLines);
    }

    [Fact]
    public void A_comma_decimal_culture_still_prints_dots()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string[] lines = Fill(new NetSample(Stats, 0, HasServerTick: true, 0.4, 2.1, 60), out int count);
            string text = Joined(lines, count);

            Assert.Contains("0.031 m", text);
            Assert.Contains("抖动 3.4 ms", text);
            Assert.DoesNotContain(",", text);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void The_clock_text_is_invariant_too()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("3:27", HudText.Timer(207 * 30));
            Assert.Equal("3/5", HudText.Parts(new PlateValues(PlateKind.TeamAlive, 3, 5, 2, 5)).LeftValue);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
