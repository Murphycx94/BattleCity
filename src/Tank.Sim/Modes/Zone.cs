using System.Numerics;
using Tank.Sim.Maps;

namespace Tank.Sim.Modes;

/// <summary>A safe circle on the ground: centre (x, z), radius m (0: closed).</summary>
public readonly record struct ZoneRing(Vector2 Centre, float Radius)
{
    /// <summary>Whether <paramref name="p"/> lies within the radius (the edge itself included); never for a closed ring.</summary>
    public bool Contains(Vector2 p) => Radius > 0f && Vector2.DistanceSquared(p, Centre) <= Radius * Radius;
}

/// <summary>One ring's part of the schedule (stage 6 design §3.1): its waiting and shrinking ticks, the radius it shrinks to, the damage outside while it applies (% of max health a second).</summary>
public readonly record struct ZoneStage(int WaitTicks, int ShrinkTicks, float Radius, int DamagePercent);

/// <summary>
/// The royale zone's timetable (stage 6 design §3.1, deviation 18): five rings, each announced when its waiting begins,
/// then shrunk to over its shrinking; the first waiting starts with the match. Constants only, never sent on the wire.
/// </summary>
public static class ZoneSchedule
{
    public const int Rings = 5;

    private const int Tps = SimConstants.TickRate;

    private static readonly ZoneStage[] Table =
    {
        new(90 * Tps, 90 * Tps, 420f, 1),
        new(60 * Tps, 60 * Tps, 250f, 2),
        new(50 * Tps, 45 * Tps, 140f, 4),
        new(40 * Tps, 35 * Tps, 60f, 6),
        new(30 * Tps, 40 * Tps, 0f, 10),
    };

    /// <summary>Ticks from the match start at which ring k's waiting begins, at index k − 1; index Rings holds <see cref="ClosedAt"/>.</summary>
    private static readonly int[] Starts = BuildStarts();

    /// <summary>Ring k (1–5) at index k − 1: (90 s, 90 s, 420 m, 1 %), (60, 60, 250, 2), (50, 45, 140, 4), (40, 35, 60, 6), (30, 40, 0, 10).</summary>
    public static ReadOnlySpan<ZoneStage> Stages => Table;

    /// <summary>Ring <paramref name="ring"/>'s (1–5) part of the schedule.</summary>
    public static ZoneStage Stage(int ring) => Table[Index(ring)];

    /// <summary>Ticks from the match start to ring <paramref name="ring"/>'s (1–5) announcement: 0, 5400, 9000, 11850, 14100.</summary>
    public static int WaitStart(int ring) => Starts[Index(ring)];

    /// <summary>Ticks from the match start to ring <paramref name="ring"/>'s shrinking: 2700, 7200, 10500, 13050, 15000.</summary>
    public static int ShrinkStart(int ring)
    {
        int index = Index(ring);   // checked before either table is read
        return Starts[index] + Table[index].WaitTicks;
    }

    /// <summary>Ticks from the match start to ring <paramref name="ring"/> being all there is (the next one's waiting): 5400, 9000, 11850, 14100, 16200.</summary>
    public static int ShrinkEnd(int ring) => Starts[Index(ring) + 1];

    /// <summary>Ticks from the match start to the zone's closing: 16200 (540 s).</summary>
    public static int ClosedAt => Starts[Rings];

    /// <summary>
    /// Rings 1–5 announced by <paramref name="elapsedTicks"/>: those whose waiting has begun (1 from the start, 5 from 470 s). Below 0
    /// reads as 0. Ring 0 isn't counted: the rings the players know are 1 + this (<see cref="MatchRules.KnownRings"/>).
    /// </summary>
    public static int AnnouncedBy(int elapsedTicks)
    {
        int announced = 1;
        while (announced < Rings && Starts[announced] <= elapsedTicks) announced++;
        return announced;
    }

    /// <summary>Ring 0: the circle round the playable rectangle (riverland: (600, 600), 806.1 m), the whole map inside it.</summary>
    public static ZoneRing Outer(Battlefield field)
    {
        ArgumentNullException.ThrowIfNull(field);
        float width = field.Terrain.SizeX - 2f * field.Border, depth = field.Terrain.SizeZ - 2f * field.Border;
        return new ZoneRing(new Vector2(field.Terrain.SizeX / 2f, field.Terrain.SizeZ / 2f), MathF.Sqrt(width * width + depth * depth) / 2f);
    }

    /// <summary>ceil(maxHealth × percent / 100) in integers; 0 for 0 %.</summary>
    public static int DamageOf(int maxHealth, int percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxHealth);
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        return (int)(((long)maxHealth * percent + 99) / 100);
    }

    /// <summary>
    /// <paramref name="rings"/> as a match takes them: exactly 1 + Rings, every radius finite and ≥ 0, every centre finite, each ring k ≥ 1
    /// inside ring k − 1 (|c_k − c_{k−1}| + r_k ≤ r_{k−1} + 0.01 m). The radii needn't be the schedule's (tests use small ones).
    /// A ring of radius 0 before ring 5 is allowed (the rings after it must then be points within the slack of its centre).
    /// Returns a copy, an array the caller may keep. ArgumentException (ArgumentNullException for null), param <paramref name="paramName"/>.
    /// </summary>
    public static ZoneRing[] Check(IReadOnlyList<ZoneRing>? rings, string paramName)
    {
        const float Slack = 0.01f;
        if (rings is null) throw new ArgumentNullException(paramName, "A royale needs its rings.");
        if (rings.Count != 1 + Rings)
            throw new ArgumentException($"A royale has {1 + Rings} rings (ring 0, then rings 1–{Rings}), not {rings.Count}.", paramName);
        var copy = new ZoneRing[rings.Count];
        for (int k = 0; k < copy.Length; k++)
        {
            ZoneRing ring = rings[k];
            if (!float.IsFinite(ring.Centre.X) || !float.IsFinite(ring.Centre.Y) || !float.IsFinite(ring.Radius) || ring.Radius < 0f)
                throw new ArgumentException($"Ring {k} {ring} needs a finite centre and a finite radius ≥ 0.", paramName);
            if (k > 0 && Vector2.Distance(ring.Centre, copy[k - 1].Centre) + ring.Radius > copy[k - 1].Radius + Slack)
                throw new ArgumentException($"Ring {k} {ring} reaches out of ring {k - 1} {copy[k - 1]}.", paramName);
            copy[k] = ring;
        }
        return copy;
    }

    private static int Index(int ring)
    {
        if (ring is < 1 or > Rings) throw new ArgumentOutOfRangeException(nameof(ring), ring, $"Rings run from 1 to {Rings}.");
        return ring - 1;
    }

    private static int[] BuildStarts()
    {
        var starts = new int[Rings + 1];
        for (int k = 0; k < Rings; k++) starts[k + 1] = starts[k] + Table[k].WaitTicks + Table[k].ShrinkTicks;
        return starts;
    }
}

public enum ZonePhase : byte { None, Waiting, Shrinking, Closed }

/// <summary>
/// The zone at one moment (stage 6 design §3.1): <see cref="Ring"/> is the ring being waited for or shrunk to (1–5; 5 once closed);
/// <see cref="Current"/> the safe circle now; <see cref="Next"/> the ring announced (waiting) or being shrunk to, when known;
/// <see cref="TicksLeft"/> the ticks left in this waiting or shrinking (0 once closed); <see cref="DamagePercent"/> what being outside
/// costs a second now (0 while ring 1 waits; ring k − 1's while ring k waits; ring k's while it shrinks; ring 5's once closed).
/// </summary>
public readonly record struct ZoneState(int Ring, ZonePhase Phase, ZoneRing Current, ZoneRing Next, bool HasNext, int TicksLeft, int DamagePercent)
{
    /// <summary>
    /// The pure function every side shares: <paramref name="rings"/>[0] is ring 0, [k] ring k, as far as known (a client's list may stop
    /// short: a ring not known yet holds the last known one as <see cref="Current"/> and gives no <see cref="Next"/>); empty → default
    /// (Phase None). <paramref name="elapsedTicks"/> from the match start, fractional for drawing, below 0 read as 0. Shrinking interpolates
    /// centre and radius linearly from ring k − 1 to ring k. No allocation: the rules call it every tick, the client every frame.
    /// </summary>
    /// <remarks>
    /// A short list (a client that hasn't been sent ring k yet, so <c>k ≥ rings.Length</c>): <see cref="Ring"/>, <see cref="Phase"/>,
    /// <see cref="TicksLeft"/> and <see cref="DamagePercent"/> still follow the clock, while <see cref="Current"/> stays the last
    /// known ring, unshrunk, and <see cref="HasNext"/> is false — the phase may read later than the circle shows until the
    /// announcement arrives. The server's list is always whole, so damage never meets this case. NaN reads as 0, +∞ as closed.
    /// </remarks>
    public static ZoneState At(ReadOnlySpan<ZoneRing> rings, float elapsedTicks)
    {
        int known = rings.Length;
        if (known == 0) return default;
        float e = elapsedTicks > 0f ? elapsedTicks : 0f;   // NaN reads as 0 too
        if (e >= ZoneSchedule.ClosedAt)
            return new ZoneState(ZoneSchedule.Rings, ZonePhase.Closed, rings[Math.Min(ZoneSchedule.Rings, known - 1)], default, false, 0,
                ZoneSchedule.Stage(ZoneSchedule.Rings).DamagePercent);

        int k = 1;
        while (e >= ZoneSchedule.ShrinkEnd(k)) k++;
        ZoneRing from = rings[Math.Min(k - 1, known - 1)];
        bool hasNext = k < known;
        ZoneRing next = hasNext ? rings[k] : default;
        int shrinkStart = ZoneSchedule.ShrinkStart(k);
        if (e < shrinkStart)
            return new ZoneState(k, ZonePhase.Waiting, from, next, hasNext, (int)MathF.Ceiling(shrinkStart - e),
                k == 1 ? 0 : ZoneSchedule.Stage(k - 1).DamagePercent);

        ZoneRing current = from;
        if (hasNext)
        {
            float t = (e - shrinkStart) / ZoneSchedule.Stage(k).ShrinkTicks;
            current = new ZoneRing(Vector2.Lerp(from.Centre, next.Centre, t), from.Radius + (next.Radius - from.Radius) * t);
        }
        return new ZoneState(k, ZonePhase.Shrinking, current, next, hasNext, (int)MathF.Ceiling(ZoneSchedule.ShrinkEnd(k) - e),
            ZoneSchedule.Stage(k).DamagePercent);
    }

    /// <summary>Outside the safe circle: farther than its radius from its centre, or anywhere once the radius is 0. False with Phase None.</summary>
    public bool IsOutside(Vector2 p) =>
        Phase != ZonePhase.None && (Current.Radius <= 0f || Vector2.DistanceSquared(p, Current.Centre) > Current.Radius * Current.Radius);

    /// <summary>Where to be: <see cref="Next"/> when known, else <see cref="Current"/>.</summary>
    public ZoneRing Target => HasNext ? Next : Current;

    /// <summary>Ticks until <see cref="Target"/> is all there is: waiting, the rest of it plus the shrink; shrinking, <see cref="TicksLeft"/>; closed or None, 0.</summary>
    public int TicksToTarget => Phase switch
    {
        ZonePhase.Waiting => TicksLeft + ZoneSchedule.Stage(Ring).ShrinkTicks,
        ZonePhase.Shrinking => TicksLeft,
        _ => 0,
    };
}
