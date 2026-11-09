using System.Numerics;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Ai;

/// <summary>
/// Supply crates in a royale (stage 6 design §4.5; stage 6B plan, task 6): the director reads the match's crates
/// (<see cref="Crates"/>) for its bots to fetch the useful ones they see on their way (Bot.Crates.cs), choosing for each
/// (<see cref="CrateFor"/>) by what the bot needs most, then by distance.
/// </summary>
public sealed partial class BotDirector
{
    /// <summary>A bot fetches crates within this of its tank, m (design §4.5: 150 m).</summary>
    public const float CrateRange = 150f;

    /// <summary>Below this share of its health a bot fetches a repair crate before any other.</summary>
    public const float CrateHealthShare = 0.6f;

    /// <summary>Below this share of its rounds (AP and HE together, over the two capacities) a bot fetches an ammo crate before a layer.</summary>
    public const float CrateAmmoShare = 0.5f;

    /// <summary>A crate run not over this many ticks after it began is given up (20 s).</summary>
    public const int CrateGiveUpTicks = 20 * SimConstants.TickRate;

    /// <summary>
    /// A crate a bot gave up (not eaten: the run timed out, no way to it, stuck three times on the way, or stopped short of
    /// it) is not picked by that bot again for this many ticks (40 s), or until the next ring (batch C review follow-up:
    /// it used to be picked again at the next look, 6 ticks on, and a crate boxed in by rocks or parked tanks held the bot
    /// where it stood, searching, for the rest of the match).
    /// </summary>
    public const int CrateShunTicks = 2 * CrateGiveUpTicks;

    /// <summary>The match's crates: null without any (every mode but a royale, and the unit tests' royales that place none).</summary>
    internal CrateSet? Crates => Rules.Crates;

    /// <summary>
    /// The crate the bot in tank <paramref name="me"/>, not fighting, should fetch, or −1: one standing, not in the bot's
    /// <paramref name="shunned"/> mask (bit = crate id: the crates it gave up lately, Bot.Crates.cs), within
    /// <see cref="CrateRange"/> (and the crew's view range), that would do something for the tank
    /// (<see cref="CrateRules.IsUseful"/>), in the ring the bot heads for (<see cref="InRing"/>; the zone's
    /// <see cref="ZoneState.Target"/>), that the time allows (the way to it and on from it to the circle a run stops at
    /// (<see cref="InsideLimit"/>), both × <see cref="ZoneDetour"/>, must not <see cref="MustRun"/>: so none once the zone
    /// has closed), and that the crew sees (as <see cref="CrateSight.Sees"/> has it: the turret top worked out once a look,
    /// the ray (<see cref="CrateSight.LineClear"/>) cast last, and only for a crate that would beat the best so far). By
    /// priority: a repair crate below <see cref="CrateHealthShare"/> of the health, then an ammo crate below
    /// <see cref="CrateAmmoShare"/> of the rounds, then any useful one; then the nearer; then the lower id. Without a zone
    /// (a royale given no rings) the ring and the time are not looked at. A wreck gets none. Given <paramref name="nearSlot"/>
    /// (≥ 0: a follower's team-mate in duos, its slot in <c>Sim.Tanks</c>), only a crate within <see cref="MateRange"/> of
    /// that tank will do (6C batch C review follow-up: a follower's crate runs took it up to 150 m from its team-mate). A
    /// loop over the table: no allocation, no random numbers (plan, deviation 15: the old seeds replay).
    /// </summary>
    internal int CrateFor(in TankState me, ulong shunned, int nearSlot = -1)
    {
        CrateSet? crates = Crates;
        if (crates is null || me.IsDestroyed) return -1;
        bool zoneOn = ZoneOn;
        ZoneState zone = Zone;
        ZoneRing target = zone.Target;
        float stopAt = InsideLimit(target);
        bool hurt = me.Health < CrateHealthShare * me.Spec.MaxHealth;
        bool low = me.ApLeft + me.HeLeft < CrateAmmoShare * (me.Spec.Ap.Capacity + me.Spec.He.Capacity);
        float range = MathF.Min(CrateRange, CrateRules.ViewRange(me));
        Vector2 near = nearSlot >= 0 ? Sim.Tanks[nearSlot].Position : default;
        Vector3 eye = Spotting.TurretTop(me, TankPose.Compute(me, Sim.Field.Terrain));
        int best = -1, bestPriority = int.MaxValue;
        float bestDistance = float.MaxValue;
        ReadOnlySpan<Crate> all = crates.All;
        for (int id = 0; id < all.Length; id++)
        {
            if (!crates.IsStanding(id) || (shunned >> id & 1ul) != 0) continue;
            ref readonly Crate crate = ref all[id];
            if (!CrateRules.IsUseful(me, crate.Kind)) continue;
            float distanceSquared = Vector2.DistanceSquared(me.Position, crate.Position);
            if (distanceSquared > range * range) continue;
            int priority = crate.Kind == CrateKind.Repair && hurt ? 0 : crate.Kind == CrateKind.Ammo && low ? 1 : 2;
            if (priority > bestPriority || (priority == bestPriority && distanceSquared >= bestDistance)) continue;
            if (nearSlot >= 0 && Vector2.DistanceSquared(crate.Position, near) > MateRange * MateRange) continue;
            if (zoneOn)
            {
                if (!InRing(crate.Position, target)) continue;
                float toCrate = MathF.Sqrt(distanceSquared) * ZoneDetour;
                float onward = MathF.Max(0f, Vector2.Distance(crate.Position, target.Centre) - stopAt) * ZoneDetour;
                if (MustRun(toCrate + onward, me.Spec.MaxForwardSpeed, zone.TicksToTarget, zone.IsOutside(crate.Position))) continue;
            }
            if (!CrateSight.LineClear(eye, crate.Position, Sim.Field)) continue;
            (best, bestPriority, bestDistance) = (id, priority, distanceSquared);
        }
        return best;
    }

    /// <summary>
    /// "3 crate runs, 2 fetched; " when the match has crates, else nothing (for <see cref="Summary"/>): the crates the bots
    /// ran for and ate (<see cref="Bot.CratesTaken"/>), not the server's end-of-match count by <see cref="CrateSet.TakenBy"/>,
    /// which has the ones driven over by chance too.
    /// </summary>
    private string CrateRunsText()
    {
        if (Crates is null) return "";
        int runs = 0, taken = 0;
        foreach (Bot bot in _bots) (runs, taken) = (runs + bot.CrateRuns, taken + bot.CratesTaken);
        return $"{runs} crate runs, {taken} fetched; ";
    }
}
