using System.Numerics;
using Tank.Sim;
using Tank.Sim.Geometry;

namespace Tank.Net.Battle;

/// <summary>Another tank, ready to draw: the newest wire data and a smoothed pose in metres and radians.</summary>
public readonly record struct RemoteTank(BattleEntity Entity, Vector2 Position, float Yaw, float TurretYaw, float GunPitch, float Speed, bool Extrapolated);

/// <summary>
/// Draws the other tanks a little in the past, between two received snapshots, so they move smoothly (design §5.1).
/// The delay adapts to measured arrival jitter and falls back to short extrapolation when snapshots stop arriving.
/// </summary>
public sealed class BattleInterpolator
{
    private const double MinDelayTicks = 3.0;           // 100 ms (design §5.1): one lost snapshot doesn't mean extrapolating
    private const double MaxDelayTicks = 6.0;
    private const double BaseDelayTicks = 1.0;          // one snapshot interval …
    private const double JitterMargin = 1.2;            // … plus the measured jitter with a margin …
    private const double HalfTickSlack = 0.5;           // … plus half a tick so the render time sits between two snapshots
    private const double MaxExtrapolationTicks = 3.0;   // 100 ms
    private const double DelayChangePerSecond = 1.0;
    private const double JitterPeakDecay = 0.99;        // per snapshot: a spike fades in a few seconds
    private const double MaxJitterSample = 0.25;        // one long stall must not pin the delay at max
    private const double SnapThresholdTicks = 8.0;
    private const int TeleportCm = 2000;                // a jump of 20 m between two snapshots is a respawn, not a move
    private const double DelayFollowPerSecond = 3.0;    // how fast the render clock closes on where it should be, 1/s
    private const float MinExtrapolationTicks = 0.01f;  // less than this ahead of the newest snapshot is not extrapolating

    private readonly BattleSnapshotStore _store;
    private int _newestTick = -1;
    private double _newestArrival;
    private int _prevTick = -1;
    private double _prevArrival;
    private bool _started;

    public BattleInterpolator(BattleSnapshotStore store) => _store = store;

    public double RenderTick { get; private set; }
    public double DelayTicks { get; private set; } = MinDelayTicks;
    public double JitterPeakSeconds { get; private set; }
    public int ExtrapolatedFrames { get; private set; }

    public void Reset()
    {
        _newestTick = _prevTick = -1;
        _newestArrival = _prevArrival = 0;
        _started = false;
        RenderTick = 0;
        ExtrapolatedFrames = 0;
        JitterPeakSeconds = 0;
        DelayTicks = MinDelayTicks;
    }

    public void OnSnapshot(int tick, double now)
    {
        if (tick <= _newestTick) return;   // reordered or duplicate: stored, but not a timing sample
        if (_prevTick >= 0)
        {
            double expected = (tick - _prevTick) / (double)SimConstants.TickRate;
            double deviation = System.Math.Min(System.Math.Abs(now - _prevArrival - expected), MaxJitterSample);
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
        double target = System.Math.Clamp(BaseDelayTicks + JitterPeakSeconds * SimConstants.TickRate * JitterMargin + HalfTickSlack, MinDelayTicks, MaxDelayTicks);
        double maxStep = DelayChangePerSecond * dt;
        DelayTicks += System.Math.Clamp(target - DelayTicks, -maxStep, maxStep);
        double estimatedServerTick = _newestTick + (now - _newestArrival) * SimConstants.TickRate;
        double wanted = estimatedServerTick - DelayTicks;
        if (!_started)
        {
            RenderTick = wanted;
            _started = true;
            return;
        }
        RenderTick += dt * SimConstants.TickRate;
        double error = wanted - RenderTick;
        if (System.Math.Abs(error) > SnapThresholdTicks) RenderTick = wanted;
        else RenderTick += error * System.Math.Min(1.0, dt * DelayFollowPerSecond);
    }

    /// <summary>Fills <paramref name="output"/> with every tank but <paramref name="ownId"/> at <see cref="RenderTick"/>.</summary>
    public void Sample(List<RemoteTank> output, int ownId)
    {
        output.Clear();
        int floor = (int)System.Math.Floor(RenderTick);
        BattleSnapshot? a = _store.FindAtOrBefore(floor);
        BattleSnapshot? b = _store.FindAfter(floor);
        if (a is null && b is null) return;
        if (a is null)
        {
            foreach (ref readonly BattleEntity e in b!.EntitySpan)
                if (e.Id != ownId) output.Add(At(e, e.X, e.Z, Yaw(e.Yaw), Yaw(e.TurretYaw), Pitch(e.Pitch), Speed(e.Speed), false));
            return;
        }
        if (b is null)
        {
            Extrapolate(a, output, ownId);
            return;
        }
        float t = (float)((RenderTick - a.Tick) / (b.Tick - a.Tick));
        foreach (ref readonly BattleEntity ea in a.EntitySpan)
        {
            if (ea.Id == ownId) continue;
            if (!b.TryFind(ea.Id, out BattleEntity eb) || Jumped(ea, eb))
            {
                BattleEntity pick = b.TryFind(ea.Id, out BattleEntity later) && t >= 0.5f ? later : ea;
                output.Add(At(pick, pick.X, pick.Z, Yaw(pick.Yaw), Yaw(pick.TurretYaw), Pitch(pick.Pitch), Speed(pick.Speed), false));
                continue;
            }
            BattleEntity latest = t < 0.5f ? ea : eb;   // discrete fields (health, flags) switch at the midpoint
            output.Add(At(latest, ea.X + (eb.X - ea.X) * t, ea.Z + (eb.Z - ea.Z) * t,
                Angles.Lerp(Yaw(ea.Yaw), Yaw(eb.Yaw), t), Angles.Lerp(Yaw(ea.TurretYaw), Yaw(eb.TurretYaw), t),
                Pitch(ea.Pitch) + (Pitch(eb.Pitch) - Pitch(ea.Pitch)) * t, Speed(ea.Speed) + (Speed(eb.Speed) - Speed(ea.Speed)) * t, false));
        }
    }

    private void Extrapolate(BattleSnapshot a, List<RemoteTank> output, int ownId)
    {
        float ahead = (float)System.Math.Min(RenderTick - a.Tick, MaxExtrapolationTicks);
        if (ahead > MinExtrapolationTicks) ExtrapolatedFrames++;
        BattleSnapshot? prev = _store.FindAtOrBefore(a.Tick - 1);
        foreach (ref readonly BattleEntity e in a.EntitySpan)
        {
            if (e.Id == ownId) continue;
            float vx = 0, vz = 0;
            if (prev is not null && prev.TryFind(e.Id, out BattleEntity p) && a.Tick > prev.Tick)
            {
                if (!Jumped(p, e))   // a respawn between the two snapshots is not a velocity to carry on with
                {
                    vx = (e.X - p.X) / (float)(a.Tick - prev.Tick);
                    vz = (e.Z - p.Z) / (float)(a.Tick - prev.Tick);
                }
            }
            output.Add(At(e, e.X + vx * ahead, e.Z + vz * ahead, Yaw(e.Yaw), Yaw(e.TurretYaw), Pitch(e.Pitch), Speed(e.Speed), ahead > MinExtrapolationTicks));
        }
    }

    /// <summary>Whether the tank moved further than <see cref="TeleportCm"/> between two snapshots (long arithmetic: a hostile coordinate can't overflow it).</summary>
    private static bool Jumped(in BattleEntity from, in BattleEntity to) =>
        System.Math.Abs((long)to.X - from.X) > TeleportCm || System.Math.Abs((long)to.Z - from.Z) > TeleportCm;

    private static RemoteTank At(in BattleEntity e, float xCm, float zCm, float yaw, float turret, float pitch, float speed, bool extrapolated) =>
        new(e, new Vector2(xCm * BattleProtocol.PositionUnit, zCm * BattleProtocol.PositionUnit), yaw, turret, pitch, speed, extrapolated);

    private static float Yaw(ushort q) => BattleProtocol.DequantizeAngle(q, BattleProtocol.YawBits);
    private static float Pitch(ushort q) => BattleProtocol.DequantizePitch(q, BattleProtocol.PitchBits);
    private static float Speed(short q) => q * BattleProtocol.SpeedUnit;
}
