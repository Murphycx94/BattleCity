using System.Globalization;
using Tank.Net.Battle;

namespace Tank.Client.Match;

/// <summary>
/// What the F3 panel reports: the client's own figures (<see cref="BattleStats"/>, resyncs) plus, when this process
/// hosts on a thread, the server's tick cost, and the frame rate the engine measures.
/// </summary>
public readonly record struct NetSample(BattleStats Stats, int Resyncs, bool HasServerTick, double ServerTickAvgMs, double ServerTickMaxMs, int Fps);

/// <summary>
/// The F3 panel's lines (blueprint §4.2), culture-invariant so a comma-decimal locale still prints dots. The caller owns
/// the array and refills it only when it wants fresh text (the panel does so a few times a second), so nothing is
/// built while the panel is closed.
/// </summary>
public static class NetPanelText
{
    public const int MaxLines = 9;
    private const double BytesPerKb = 1024.0;

    /// <summary>Writes the lines for <paramref name="s"/> into <paramref name="into"/> (at least <see cref="MaxLines"/> long) and returns how many.</summary>
    public static int Lines(in NetSample s, string[] into)
    {
        BattleStats t = s.Stats;
        CultureInfo c = CultureInfo.InvariantCulture;
        int n = 0;
        into[n++] = string.Create(c, $"延迟 {t.RttMs} ms · 抖动 {t.JitterMs:0.0} ms · 插值 {t.InterpDelayMs:0} ms");
        into[n++] = string.Create(c, $"快照 {t.SnapshotsPerSecond}/s · {t.SnapshotBytes} B");
        into[n++] = string.Create(c, $"下行 {t.BytesInPerSecond / BytesPerKb:0.0} KB/s · 上行 {t.BytesOutPerSecond / BytesPerKb:0.0} KB/s");
        into[n++] = string.Create(c, $"时钟 ×{t.ClockRate:0.000} · 余量 {t.InputSlackTicks:0.0} tick");
        into[n++] = string.Create(c, $"对账 {t.Corrections} 次 · 最近 {t.LastCorrection:0.000} m");
        into[n++] = string.Create(c, $"修正 平均 {t.MeanCorrection:0.000} m · 最大 {t.MaxCorrection:0.000} m");
        into[n++] = string.Create(c, $"丢包 {t.DroppedPackets} · 重同步 {s.Resyncs}");
        if (s.HasServerTick) into[n++] = string.Create(c, $"服务器 tick 平均 {s.ServerTickAvgMs:0.0} ms · 最大 {s.ServerTickMaxMs:0.0} ms");
        into[n++] = string.Create(c, $"帧率 {s.Fps} fps");
        return n;
    }
}
