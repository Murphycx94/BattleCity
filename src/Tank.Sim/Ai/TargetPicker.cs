using System.Numerics;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Ai;

/// <summary>
/// One bot's choice of enemy (stage 5 design §4.3). A candidate is an enemy that is alive, that the bot's team sees
/// (<see cref="VisionTracker.IsVisible"/>: no cheating), within <see cref="EngageRange"/> on the ground, that the gun can
/// reach in pitch and that the bot's own trunnion can see: the line to the aim point is clear of ground, rocks, houses
/// and walls, or ends on them within <see cref="SightSlack"/> of it, and clear of wrecks, which stop shells too. The aim
/// point is the one <see cref="Gunnery.Aim"/> lays on (the hull centre, or the turret when the gun can't reach the hull
/// centre), worked out for the round the bot fires: AP, then HE once the AP is gone; a bot out of both has none (5C
/// stalemate fix 2). Ranking: one that hit the bot in the last <see cref="HitMemoryTicks"/>, then the lowest share of its
/// health, then the nearest on the ground, then the lower tank id. A pick is held for <see cref="HoldTicks"/>; after that
/// it is reviewed every <see cref="VisionTracker.UpdateInterval"/> ticks and changed only for one that ranks ahead. One
/// that stops being a candidate is kept, <see cref="TargetLost"/> (never fired at), for
/// <see cref="VisionTracker.UpdateInterval"/> ticks before it is dropped, so a target at the edge of range or sight
/// doesn't flip the bot between engaging and driving every tick; one destroyed or gone is dropped at once. Either way the
/// bot then picks again at once. Without a target it looks once every <see cref="VisionTracker.UpdateInterval"/> ticks,
/// as often as what its team sees changes (5C batch B review follow-up); finding no candidate, it names the nearest enemy
/// within <see cref="CloseRange"/> that would be one but for the gun's pitch and that it would still see from a few metres
/// further off (<see cref="TooClose"/>), for the bot to back away from, and keeps naming it further off until it is one
/// (5C stalemate fix 2). Deterministic (simulation state only) and allocation-free after construction.
/// </summary>
public sealed class TargetPicker
{
    public const float EngageRange = 400f;

    /// <summary>An enemy the gun can't be laid on is named (<see cref="TooClose"/>) this close on the ground, m.</summary>
    public const float CloseRange = 30f;

    /// <summary>A target is kept this long unless it is lost.</summary>
    public const int HoldTicks = 2 * SimConstants.TickRate;

    /// <summary>"Hit me lately": a hit noted no more than this many ticks back.</summary>
    public const int HitMemoryTicks = 3 * SimConstants.TickRate;

    /// <summary>A sight line stopped this close to the aim point is clear: what it hit is the ground at the target, m.</summary>
    public const float SightSlack = 3f;

    /// <summary>The attackers remembered; a new one takes the oldest slot.</summary>
    private const int HitSlots = 4;

    private readonly int[] _hitBy = new int[HitSlots];   // attacker ids; 0 = empty
    private readonly int[] _hitAt = new int[HitSlots];
    private int _reviewAt;   // the tick from which the current target is reviewed (without one: looked for)
    private int _lostAt = -1;   // the tick the target held stopped being a candidate; −1: it is one

    public TargetPicker(int tankId) => TankId = tankId;

    public int TankId { get; }

    /// <summary>The enemy picked, or 0 for none.</summary>
    public int TargetId { get; private set; }

    /// <summary>The target held isn't a candidate now: kept a moment (see the summary), not to be fired at.</summary>
    public bool TargetLost => _lostAt >= 0;

    /// <summary>
    /// Without a target: the nearest enemy at the last look that would be a candidate but that the gun can't be laid on
    /// (too close and too low, or too high; in sight all the same, and from a few metres further off too), within <see cref="CloseRange"/>, or further when it was
    /// the one named at the look before (the bot backing away from it gets it named until it can lay on it); 0 for none,
    /// and always 0 with a target. It may have been destroyed or have moved since.
    /// </summary>
    public int TooClose { get; private set; }

    /// <summary>
    /// A TankHit or SplashDamage on this bot from <paramref name="attackerId"/> at <paramref name="tick"/>; any hit counts,
    /// one that did no damage too. The attacker's slot is brought up to date, or the oldest slot (an empty one first) taken.
    /// </summary>
    public void NoteHit(int attackerId, int tick)
    {
        if (attackerId <= 0) return;
        int slot = 0;
        for (int i = 0; i < HitSlots; i++)
        {
            if (_hitBy[i] == attackerId)
            {
                _hitAt[i] = System.Math.Max(_hitAt[i], tick);
                return;
            }
            if (_hitBy[slot] != 0 && (_hitBy[i] == 0 || _hitAt[i] < _hitAt[slot])) slot = i;
        }
        _hitBy[slot] = attackerId;
        _hitAt[slot] = tick;
    }

    /// <summary>
    /// The target for <paramref name="tick"/>: the one held, unless the hold is over or it was lost (and kept a moment
    /// no longer); otherwise the best candidate (0 when there is none), looked for at most every
    /// <see cref="VisionTracker.UpdateInterval"/> ticks without a target. Returns <see cref="TargetId"/>. A bot that is
    /// gone, wrecked or out of both AP and HE has none.
    /// </summary>
    public int Pick(Simulation sim, VisionTracker vision, int tick)
    {
        int slot = sim.SlotOf(TankId);
        if (slot < 0) return Drop();
        ref readonly TankState me = ref sim.Tanks[slot];
        if (me.IsDestroyed || (me.ApLeft == 0 && me.HeLeft == 0)) return Drop();
        var looker = new Looker(me, sim.Field);
        if (TargetId != 0)
        {
            int held = sim.SlotOf(TargetId);
            if (held < 0 || sim.Tanks[held].IsDestroyed) Drop();
            else if (Fit(looker, sim.Tanks, sim.Tanks[held], vision, out _) == Fitness.Candidate)
            {
                _lostAt = -1;
                if (tick < _reviewAt) return TargetId;
            }
            else
            {
                if (_lostAt < 0) _lostAt = tick;
                if (tick - _lostAt < VisionTracker.UpdateInterval) return TargetId;
                Drop();
            }
        }
        else if (tick < _reviewAt) return 0;   // nothing at the last look, and what the team sees hasn't changed since
        int best = Best(looker, sim.Tanks, vision, tick);
        if (best == TargetId)
        {
            _reviewAt = tick + VisionTracker.UpdateInterval;   // still the best (or still nothing): look again later
            return TargetId;
        }
        TargetId = best;
        _reviewAt = tick + HoldTicks;
        return TargetId;
    }

    /// <summary>Forgets the target, the hold and the hits (the bot was knocked out, or is starting over).</summary>
    public void Clear()
    {
        Drop();
        Array.Clear(_hitBy);
        Array.Clear(_hitAt);
    }

    private int Drop()
    {
        (TargetId, _reviewAt, _lostAt, TooClose) = (0, 0, -1, 0);
        return 0;
    }

    /// <summary>The bot as it looks out this tick: where its trunnion is and how fast its round flies, worked out once for every enemy.</summary>
    private readonly struct Looker
    {
        public readonly TankState Me;
        public readonly TankPose Pose;
        public readonly Vector3 Trunnion;
        public readonly float ShellSpeed;
        public readonly Battlefield Field;

        public Looker(in TankState me, Battlefield field)
        {
            Me = me;
            Field = field;
            Pose = TankPose.Compute(me, field.Terrain);
            Trunnion = Pose.Trunnion(me.Spec);
            ShellSpeed = Gunnery.ShellSpeed(me);
        }
    }

    /// <summary>What a tank is to the bot: not an enemy to fire at, one in sight but for the gun's pitch, or a candidate.</summary>
    private enum Fitness : byte { None, Unlaid, Candidate }

    private static Fitness Fit(in Looker looker, ReadOnlySpan<TankState> tanks, in TankState enemy, VisionTracker vision, out Vector3 aim)
    {
        ref readonly TankState me = ref looker.Me;
        aim = default;
        if (enemy.Team == me.Team || enemy.IsDestroyed) return Fitness.None;
        if (Vector2.DistanceSquared(enemy.Position, me.Position) > EngageRange * EngageRange) return Fitness.None;
        if (!vision.IsVisible(enemy.Id, me.Team)) return Fitness.None;
        Lay lay = Gunnery.Aim(me, looker.Pose, looker.Trunnion, looker.ShellSpeed, enemy, looker.Field.Terrain, out aim);
        if (looker.Field.Raycast(looker.Trunnion, aim, out RayHit hit) && Vector3.Distance(hit.Point, aim) >= SightSlack) return Fitness.None;
        if (WreckInTheWay(tanks, looker.Field.Terrain, looker.Trunnion, aim)) return Fitness.None;
        return lay.CanLay ? Fitness.Candidate : Fitness.Unlaid;
    }

    /// <summary>How far back <see cref="InSightFurtherOff"/> looks from, m.</summary>
    private const float BackOffProbe = 6f;

    /// <summary>
    /// Whether the bot would still see <paramref name="aim"/> from <see cref="BackOffProbe"/> straight back from
    /// <paramref name="enemy"/> (its trunnion moved along the ground): backing away would help. Off the edge of a rise
    /// above an enemy it wouldn't, the crest coming between them; nor with a house or a rock right behind it. Two bots
    /// on either side of a crest otherwise both backed away out of each other's sight, came back, and did it again. Asked
    /// only before an enemy is first named: once backing away, the bot carries on whatever the ground behind it.
    /// </summary>
    private static bool InSightFurtherOff(in Looker looker, in TankState enemy, Vector3 aim)
    {
        Vector2 from = looker.Me.Position - enemy.Position;
        if (from.LengthSquared() < 1e-6f) return false;
        Vector2 back = Vector2.Normalize(from) * BackOffProbe, there = looker.Me.Position + back;
        HeightField terrain = looker.Field.Terrain;
        float rise = terrain.HeightAt(there.X, there.Y) - terrain.HeightAt(looker.Me.Position.X, looker.Me.Position.Y);
        Vector3 trunnion = looker.Trunnion + new Vector3(back.X, rise, back.Y);
        return !looker.Field.Raycast(trunnion, aim, out RayHit hit) || Vector3.Distance(hit.Point, aim) < SightSlack;
    }

    /// <summary>A tank's armour boxes reach no further than this from its position on the ground, m (a tank destroyer's casemate about 4).</summary>
    private const float WreckReach = 6f;

    /// <summary>Whether the line <paramref name="from"/>→<paramref name="to"/> crosses a wreck's hull or turret box: a shell would stop on it.</summary>
    private static bool WreckInTheWay(ReadOnlySpan<TankState> tanks, HeightField terrain, Vector3 from, Vector3 to)
    {
        Vector2 a = new(from.X, from.Z), ab = new Vector2(to.X, to.Z) - a;
        float length2 = ab.LengthSquared();
        for (int i = 0; i < tanks.Length; i++)
        {
            ref readonly TankState wreck = ref tanks[i];
            if (!wreck.IsDestroyed) continue;
            float along = length2 > 0f ? System.Math.Clamp(Vector2.Dot(wreck.Position - a, ab) / length2, 0f, 1f) : 0f;
            if (Vector2.DistanceSquared(wreck.Position, a + ab * along) > WreckReach * WreckReach) continue;   // nowhere near the line
            TankPose pose = TankPose.Compute(wreck, terrain);
            if (pose.HullBox(wreck.Spec).Raycast(from, to, out _, out _) || pose.TurretBox(wreck.Spec, wreck.TurretYaw).Raycast(from, to, out _, out _))
                return true;
        }
        return false;
    }

    /// <summary>The best candidate's id, or 0; <see cref="TooClose"/> set for none.</summary>
    private int Best(in Looker looker, ReadOnlySpan<TankState> tanks, VisionTracker vision, int tick)
    {
        int best = -1, close = -1;
        bool bestHit = false;
        float closest = 0f;
        for (int i = 0; i < tanks.Length; i++)
        {
            ref readonly TankState enemy = ref tanks[i];
            Fitness fit = Fit(looker, tanks, enemy, vision, out Vector3 aim);
            if (fit == Fitness.Unlaid)
            {
                float d = Vector2.DistanceSquared(enemy.Position, looker.Me.Position);
                bool named = enemy.Id == TooClose   // backing away from it: named until it can be laid on
                             || (d <= CloseRange * CloseRange && InSightFurtherOff(looker, enemy, aim));
                if (named && (close < 0 || d < closest)) (close, closest) = (i, d);   // the first of equals
            }
            if (fit != Fitness.Candidate) continue;
            bool hit = HitLately(enemy.Id, tick);
            if (best < 0 || Outranks(looker.Me, enemy, hit, tanks[best], bestHit)) (best, bestHit) = (i, hit);
        }
        TooClose = best < 0 && close >= 0 ? tanks[close].Id : 0;
        return best < 0 ? 0 : tanks[best].Id;
    }

    /// <summary>Whether <paramref name="a"/> ranks ahead of <paramref name="b"/>: hit me lately, weaker share, nearer, lower id.</summary>
    private static bool Outranks(in TankState me, in TankState a, bool aHit, in TankState b, bool bHit)
    {
        if (aHit != bHit) return aHit;
        long aShare = (long)a.Health * b.Spec.MaxHealth, bShare = (long)b.Health * a.Spec.MaxHealth;   // exact, in integers
        if (aShare != bShare) return aShare < bShare;
        float aDistance = Vector2.DistanceSquared(a.Position, me.Position), bDistance = Vector2.DistanceSquared(b.Position, me.Position);
        if (aDistance != bDistance) return aDistance < bDistance;
        return a.Id < b.Id;
    }

    private bool HitLately(int tankId, int tick)
    {
        for (int i = 0; i < HitSlots; i++)
            if (_hitBy[i] == tankId && tick - _hitAt[i] <= HitMemoryTicks) return true;
        return false;
    }
}
