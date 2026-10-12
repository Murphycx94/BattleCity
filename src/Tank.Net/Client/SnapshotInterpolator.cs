using Tank.Core;
using Tank.Net.Protocol;

namespace Tank.Net.Client;

/// <summary>
/// Renders remote entities slightly in the past, between two received snapshots, so they move perfectly
/// smoothly. The delay adapts to measured arrival jitter (docs/03-netcode.md §5.2) and falls back to short
/// extrapolation when snapshots stop arriving.
/// </summary>
public sealed class SnapshotInterpolator
{
    private const double MinDelayTicks = 1.5;
    private const double MaxDelayTicks = 6.0;
    private const double MaxExtrapolationTicks = 6.0;   // 100ms
    private const double DelayChangePerSecond = 1.0;
    private const double JitterPeakDecay = 0.99;        // per snapshot: a spike fades in ~1s
    private const double MaxJitterSample = 0.25;        // one long stall must not pin the delay at max
    private const double SnapThresholdTicks = 8.0;

    private readonly SnapshotStore _store;
    private int _newestTick = -1;
    private double _newestArrival;
    private int _prevTick = -1;
    private double _prevArrival;
    private bool _started;

    public SnapshotInterpolator(SnapshotStore store) => _store = store;

    public double RenderTick { get; private set; }
    public double DelayTicks { get; private set; } = 2.0;
    public double TargetDelayTicks { get; private set; } = 2.0;
    public double JitterPeakSeconds { get; private set; }
    public int ExtrapolatedFrames { get; private set; }

    public void Reset()
    {
        _newestTick = _prevTick = -1;
        _started = false;
        JitterPeakSeconds = 0;
        DelayTicks = TargetDelayTicks = 2.0;
    }

    public void OnSnapshot(int tick, double now)
    {
        if (tick <= _newestTick) return; // reordered/duplicate: still stored, but not a timing sample
        if (_prevTick >= 0)
        {
            double expected = (tick - _prevTick) / (double)Units.TickRate;
            double actual = now - _prevArrival;
            double deviation = System.Math.Min(System.Math.Abs(actual - expected), MaxJitterSample);
            JitterPeakSeconds = System.Math.Max(deviation, JitterPeakSeconds * JitterPeakDecay);
        }
        _prevTick = tick;
        _prevArrival = now;
        _newestTick = tick;
        _newestArrival = now;
    }

    public void Advance(double dt, double now)
    {
        if (_newestTick < 0) return;
        TargetDelayTicks = System.Math.Clamp(1.0 + JitterPeakSeconds * Units.TickRate * 1.2 + 0.5, MinDelayTicks, MaxDelayTicks);
        double maxStep = DelayChangePerSecond * dt;
        DelayTicks += System.Math.Clamp(TargetDelayTicks - DelayTicks, -maxStep, maxStep);

        double estimatedServerTick = _newestTick + (now - _newestArrival) * Units.TickRate;
        double target = estimatedServerTick - DelayTicks;
        if (!_started)
        {
            RenderTick = target;
            _started = true;
            return;
        }
        RenderTick += dt * Units.TickRate;
        double error = target - RenderTick;
        if (System.Math.Abs(error) > SnapThresholdTicks) RenderTick = target;
        else RenderTick += error * System.Math.Min(1.0, dt * 3.0);
    }

    /// <summary>Fills <paramref name="output"/> with entities at <see cref="RenderTick"/>.</summary>
    public void Sample(List<RenderEntity> output, Func<NetEntity, bool>? include = null)
    {
        output.Clear();
        int floor = (int)System.Math.Floor(RenderTick);
        var a = _store.FindAtOrBefore(floor);
        var b = _store.FindAfter(floor);
        if (a is null && b is null) return;

        if (a is null)
        {
            foreach (var e in b!.EntitySpan)
                if (include?.Invoke(e) ?? true) output.Add(new RenderEntity(e, e.X, e.Z, false));
            return;
        }
        if (b is null)
        {
            Extrapolate(a, output, include);
            return;
        }

        double t = (RenderTick - a.Tick) / (b.Tick - a.Tick);
        foreach (var ea in a.EntitySpan)
        {
            if (!(include?.Invoke(ea) ?? true)) continue;
            if (!b.TryFind(ea.Id, out var eb))
            {
                output.Add(new RenderEntity(ea, ea.X, ea.Z, false)); // removed between a and b
                continue;
            }
            bool teleport = System.Math.Abs(eb.X - ea.X) > Units.Tile || System.Math.Abs(eb.Z - ea.Z) > Units.Tile;
            if (teleport)
            {
                var pick = t < 0.5 ? ea : eb;
                output.Add(new RenderEntity(t < 0.5 ? ea : eb, pick.X, pick.Z, false));
                continue;
            }
            var latest = t < 0.5 ? ea : eb; // discrete fields (dir, flags) switch at the midpoint
            output.Add(new RenderEntity(latest, ea.X + (eb.X - ea.X) * t, ea.Z + (eb.Z - ea.Z) * t, false));
        }
    }

    private void Extrapolate(Snapshot a, List<RenderEntity> output, Func<NetEntity, bool>? include)
    {
        double ahead = System.Math.Min(RenderTick - a.Tick, MaxExtrapolationTicks);
        if (ahead > 0.01) ExtrapolatedFrames++;
        var prev = _store.FindAtOrBefore(a.Tick - 1);
        foreach (var e in a.EntitySpan)
        {
            if (!(include?.Invoke(e) ?? true)) continue;
            double vx = 0, vz = 0;
            if (prev is not null && prev.TryFind(e.Id, out var p) && a.Tick > prev.Tick)
            {
                vx = (e.X - p.X) / (double)(a.Tick - prev.Tick);
                vz = (e.Z - p.Z) / (double)(a.Tick - prev.Tick);
                if (System.Math.Abs(vx) > Units.Tile || System.Math.Abs(vz) > Units.Tile) vx = vz = 0;
            }
            output.Add(new RenderEntity(e, e.X + vx * ahead, e.Z + vz * ahead, ahead > 0.01));
        }
    }
}
