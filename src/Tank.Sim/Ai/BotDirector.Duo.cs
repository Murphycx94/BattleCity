using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>
/// Team-mates in duos (stage 6 design §5; stage 6C plan, task 5): which tank is a bot's team-mate (<see cref="MateSlotOf"/>),
/// which of the two follows the other (<see cref="Follows"/>: the bot of a person, and of two bots the higher id), and the enemy
/// the team-mate is fighting (<see cref="MateTarget"/>), for the bot's picker to prefer. A person's is the enemy it last hit:
/// the director notes every tank's last hit on another team as it hears the step's events (<see cref="NoteLastHit"/>).
/// Outside duos none of it applies. No allocation; the simulation's state only.
/// </summary>
public sealed partial class BotDirector
{
    /// <summary>A bot heads for its live team-mate once farther off than this, m.</summary>
    public const float MateRange = 60f;

    /// <summary>Heading for its team-mate, a bot is there once within this, m.</summary>
    public const float MateArrive = 25f;

    /// <summary>Running the zone, a follower's point lies this much nearer the ring's centre than its leader's, m (half the way to the centre at most).</summary>
    public const float ZoneMateSpread = 12f;

    /// <summary>
    /// A regroup given up (stuck, no way, or a way out of the ring), a bot begins none for this many ticks (5 s), and its
    /// leader turns back for it meanwhile.
    /// </summary>
    public const int RegroupQuietTicks = 5 * SimConstants.TickRate;

    private bool _duos;

    // Each tank's last hit (duos): the attacker's id, whom it hit and when; slots taken in order, the oldest given up when full.
    private readonly int[] _hitter = new int[Simulation.MaxTanks];
    private readonly int[] _hitTarget = new int[Simulation.MaxTanks];
    private readonly int[] _hitTick = new int[Simulation.MaxTanks];

    /// <summary>Duos: the bots keep with their team-mates.</summary>
    internal bool Duos => _duos;

    /// <summary>Once in the constructor: whether the bots have team-mates to keep with.</summary>
    private void SetUpDuos() => _duos = ModeRules.IsDuos(Rules.Mode);

    /// <summary>Duos: the slot in <c>Sim.Tanks</c> of <paramref name="me"/>'s team-mate (alive or a wreck), or −1 (outside duos, or none).</summary>
    internal int MateSlotOf(in TankState me)
    {
        if (!_duos) return -1;
        ReadOnlySpan<TankState> tanks = Sim.Tanks;
        for (int i = 0; i < tanks.Length; i++)
            if (tanks[i].Team == me.Team && tanks[i].Id != me.Id) return i;
        return -1;
    }

    /// <summary>Duos: whether the bot <paramref name="bot"/> follows its team-mate: the mate is alive and is a person, or a bot with a lower tank id.</summary>
    internal bool Follows(Bot bot, in TankState me)
    {
        int slot = MateSlotOf(me);
        if (slot < 0) return false;
        ref readonly TankState mate = ref Sim.Tanks[slot];
        return !mate.IsDestroyed && (mate.Id < bot.TankId || Find(mate.Id) is null);
    }

    /// <summary>
    /// The enemy <paramref name="me"/>'s team-mate is fighting, for the picker to prefer (0: none): a bot mate's <see cref="Bot.TargetId"/>
    /// unless that target is lost (<see cref="TargetPicker.TargetLost"/>: kept a moment, not fired at); a person's, the enemy
    /// it last hit (TankHit or SplashDamage) within <see cref="TargetPicker.HitMemoryTicks"/> of <paramref name="tick"/>,
    /// while that tank is alive. A wrecked team-mate fights nobody.
    /// </summary>
    internal int MateTarget(in TankState me, int tick)
    {
        int slot = MateSlotOf(me);
        if (slot < 0) return 0;
        ref readonly TankState mate = ref Sim.Tanks[slot];
        if (mate.IsDestroyed) return 0;
        if (Find(mate.Id) is { } bot) return bot.Picker.TargetLost ? 0 : bot.TargetId;
        for (int i = 0; i < _hitter.Length; i++)
        {
            if (_hitter[i] != mate.Id) continue;
            if (tick - _hitTick[i] > TargetPicker.HitMemoryTicks) return 0;
            int target = Sim.SlotOf(_hitTarget[i]);
            return target >= 0 && !Sim.Tanks[target].IsDestroyed ? _hitTarget[i] : 0;
        }
        return 0;
    }

    /// <summary>
    /// Duos: <paramref name="attackerId"/> hit <paramref name="targetId"/> at <paramref name="tick"/>; its slot brought up to
    /// date, or an empty one (the oldest when none is) taken. A hit on its own team (friendly fire, its HE's splash) is not
    /// noted: it would take the place of the enemy it last hit.
    /// </summary>
    private void NoteLastHit(int attackerId, int targetId, int tick)
    {
        if (!_duos || attackerId <= 0 || targetId == attackerId) return;
        int from = Sim.SlotOf(attackerId), to = Sim.SlotOf(targetId);
        if (from >= 0 && to >= 0 && Sim.Tanks[from].Team == Sim.Tanks[to].Team) return;
        int slot = 0;
        for (int i = 0; i < _hitter.Length; i++)
        {
            if (_hitter[i] == attackerId)
            {
                slot = i;
                break;
            }
            if (_hitter[slot] != 0 && (_hitter[i] == 0 || _hitTick[i] < _hitTick[slot])) slot = i;
        }
        (_hitter[slot], _hitTarget[slot], _hitTick[slot]) = (attackerId, targetId, tick);
    }

    /// <summary>"12 regroups; " in duos, else nothing (for <see cref="Summary"/>).</summary>
    private string RegroupsText()
    {
        if (!_duos) return "";
        int regroups = 0;
        foreach (Bot bot in _bots) regroups += bot.Regroups;
        return $"{regroups} regroups; ";
    }
}
