using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Modes;

public enum MatchPhase : byte { Playing, Ended }

/// <summary>One player's part in the match, as the rules keep it (and the scoreboard shows it).</summary>
public struct Standing
{
    public bool Active;        // in the match (joined, not left)
    public int TankId;
    public byte Team;
    public int Kills;
    public int Deaths;
    public bool Alive;         // false while a wreck, or once eliminated
    public int RespawnAt;      // tick the tank comes back (team battle), or -1
    public int EliminatedAt;   // tick the player went out of a no-respawn match (or left it), or -1
    public byte Rank;          // final place in a free-for-all or a royale, 1 best; 0 until the match ends
    public int Damage;         // damage dealt to other sides' tanks, each hit counted up to the health its target had left (every mode)
}

/// <summary>
/// Runs a match's rules on top of a <see cref="Simulation"/> (stage 4 design §2, stage 5 design §3.1): puts players'
/// tanks on spawn points with protection, scores kills, counts base capture (standard battle), respawns or eliminates
/// the fallen, keeps the clock and calls the result and why. Players are known by room slot. Call <see cref="Update"/>
/// after every <see cref="Simulation.Step"/>.
/// </summary>
public sealed class MatchRules
{
    public const byte NoTeam = byte.MaxValue;
    public const byte NoSlot = byte.MaxValue;

    /// <summary>A spawn point is in use while a tank or wreck lies within this of it, m.</summary>
    private const float SpawnBusyRadius = 8f;

    /// <summary>With every point in use, the roomiest is tried this far to either side, m, so no two tanks share a spot.</summary>
    private const float SpawnShunt = 6f;
    private const int SpawnShunts = 3;

    /// <summary><c>XorShift(seed ^ SpawnSalt)</c> shuffles a royale's spawn order.</summary>
    public const uint SpawnSalt = 0x5A17_0C5Eu;

    private readonly Simulation _sim;
    private readonly SpawnLayout _spawns;
    private readonly Standing[] _players = new Standing[ModeRules.MaxPlayers];
    private readonly byte[] _rankScratch = new byte[ModeRules.MaxPlayers];
    private readonly int[] _teamScore = new int[2];   // kills by team, kept when a player leaves

    /// <summary>Each seat's tank's health as of the end of the last <see cref="Update"/> (0 for no tank), unchanged during an update.</summary>
    private readonly int[] _health = new int[ModeRules.MaxPlayers];

    /// <summary>What is left for this update's hits to take, per seat: <see cref="_health"/>, less each hit counted so far.</summary>
    private readonly int[] _hitRoom = new int[ModeRules.MaxPlayers];

    /// <summary>Each seat's <see cref="_health"/> on the tick its tank went out (or left): ranks those who fell on one tick.</summary>
    private readonly int[] _healthBeforeFall = new int[ModeRules.MaxPlayers];

    /// <summary>Bit <c>slot</c> set once that seat has joined this match: <see cref="Entrants"/>.</summary>
    private uint _joinedSeats;

    /// <summary>The last whole second of the match (since the start) the zone has hurt on: each one is hurt on exactly once.</summary>
    private int _lastZoneSecond;

    /// <summary>A royale's six rings (ring 0, then rings 1–5), or empty.</summary>
    private readonly ZoneRing[] _zoneRings = Array.Empty<ZoneRing>();

    /// <summary>The order a royale tries its solo points in (indices into <see cref="SpawnLayout.Solo"/>); null in the other modes, which take them in list order.</summary>
    private readonly int[]? _spawnOrder;

    /// <param name="durationTicks">A match length other than the mode's (tests), positive; null for the mode's own.</param>
    /// <param name="bases">The map's bases: required (<see cref="TeamBase.Check"/>, not empty) in a standard match, ignored in the others.</param>
    /// <param name="seed">The match seed: in a royale the solo spawn points are taken in an order drawn from it; ignored otherwise.</param>
    /// <param name="zone">
    /// A royale's six rings (<see cref="ZoneSchedule.Check"/>: ArgumentException, param "zone"): ring 0, then rings 1–5. Null: no zone
    /// (unit tests; the server always passes one). Ignored outside a royale.
    /// </param>
    public MatchRules(BattleMode mode, Simulation sim, SpawnLayout spawns, int? durationTicks = null, IReadOnlyList<TeamBase>? bases = null,
        uint seed = 0, IReadOnlyList<ZoneRing>? zone = null)
    {
        if (durationTicks is <= 0) throw new ArgumentOutOfRangeException(nameof(durationTicks), durationTicks, "A match lasts at least one tick.");
        if (ModeRules.HasBases(mode)) Capture = new BaseCapture(bases ?? Array.Empty<TeamBase>());   // checks them: ArgumentException, param "bases"
        (Mode, _sim, _spawns) = (mode, sim, spawns);
        StartTick = sim.Tick;
        EndTick = sim.Tick + (durationTicks ?? ModeRules.DurationTicks(mode));
        for (int i = 0; i < _players.Length; i++) _players[i] = new Standing { RespawnAt = -1, EliminatedAt = -1 };
        if (mode == BattleMode.Royale) _spawnOrder = ShuffledSpawns(spawns.Solo.Count, seed);
        if (ModeRules.HasZone(mode) && zone is not null) _zoneRings = ZoneSchedule.Check(zone, nameof(zone));   // a copy already
        ZoneRings = Array.AsReadOnly(_zoneRings);
        UpdateZone();
    }

    /// <summary>0 … <paramref name="count"/> − 1 in a Fisher–Yates order drawn from <c>XorShift(seed ^ SpawnSalt)</c>.</summary>
    private static int[] ShuffledSpawns(int count, uint seed)
    {
        var order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        var rng = new XorShift(seed ^ SpawnSalt);
        for (int i = count - 1; i > 0; i--)
        {
            int j = (int)(rng.NextUInt() % (uint)(i + 1));
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }

    public BattleMode Mode { get; }

    /// <summary>The simulation tick the match started at.</summary>
    public int StartTick { get; }

    /// <summary>
    /// The seats that have joined this match, those who have since left included (a royale: those it started with): each seat
    /// once however often it leaves and comes back (a team battle takes players mid-match), so never more than
    /// <see cref="ModeRules.MaxPlayers"/>. A royale's "共 N 名".
    /// </summary>
    public int Entrants => BitOperations.PopCount(_joinedSeats);
    public MatchPhase Phase { get; private set; } = MatchPhase.Playing;

    /// <summary>The tick the clock runs out.</summary>
    public int EndTick { get; }

    /// <summary>The tick the match ended, or -1 while it is on.</summary>
    public int EndedAt { get; private set; } = -1;

    public int TicksLeft => Math.Max(0, EndTick - _sim.Tick);

    /// <summary>The winning team (two-team modes), or <see cref="NoTeam"/> for a draw or while the match is on.</summary>
    public byte WinnerTeam { get; private set; } = NoTeam;

    /// <summary>The winning player's slot (free-for-all, royale), or <see cref="NoSlot"/>.</summary>
    public byte WinnerSlot { get; private set; } = NoSlot;

    /// <summary>Why the match ended, in every mode; <see cref="MatchEndReason.None"/> while it is on.</summary>
    public MatchEndReason EndReason { get; private set; }

    /// <summary>The base capture count: non-null exactly in a standard match.</summary>
    public BaseCapture? Capture { get; }

    /// <summary>
    /// The most whole points <paramref name="slot"/>'s tank held on the enemy base this match (0–100); 0 outside a
    /// standard match or for an empty seat. A player who left reads 0: the capture count forgets a tank taken out of
    /// the battle (the server sends no row for them anyway).
    /// </summary>
    public byte CaptureBest(byte slot) => Capture?.BestOf(_players[slot].TankId) ?? 0;

    public ref readonly Standing Of(byte slot) => ref _players[slot];

    /// <summary>
    /// How long <paramref name="slot"/>'s tank lasted: to its going out, or to the end (or now) if still in; 0 for an empty seat. Meaningful
    /// only without respawns or mid-match joins (a royale, a free-for-all): a team battle's respawned tank reads as in from the start,
    /// and a seat that joined late reads from the start too.
    /// </summary>
    public int LastedTicks(byte slot)
    {
        ref readonly Standing p = ref _players[slot];
        if (p.TankId == 0) return 0;
        if (p.EliminatedAt >= 0) return p.EliminatedAt - StartTick;
        return (EndedAt >= 0 ? EndedAt : _sim.Tick) - StartTick;
    }

    /// <summary>A royale's six rings (ring 0, then rings 1–5), as the server picked them; empty without a zone.</summary>
    public IReadOnlyList<ZoneRing> ZoneRings { get; }

    /// <summary>The zone at the current tick as of the last <see cref="Update"/> (or the start); Phase None without a zone. Stays put once the match has ended.</summary>
    public ZoneState Zone { get; private set; }

    /// <summary>
    /// How many of <see cref="ZoneRings"/> the players know now, ring 0 included: 1 + <see cref="ZoneSchedule.AnnouncedBy"/> (which
    /// counts rings 1–5 only), so 2 from the start and 6 from ring 5's waiting; 0 without a zone. The server sends rings up to it.
    /// </summary>
    public int KnownRings { get; private set; }

    /// <summary>The rings the players know: <see cref="ZoneRings"/>[..<see cref="KnownRings"/>].</summary>
    public ReadOnlySpan<ZoneRing> AnnouncedRings => _zoneRings.AsSpan(0, KnownRings);

    /// <summary>Whether another player may be put into the match now.</summary>
    public bool AcceptsJoins => Phase == MatchPhase.Playing && (_sim.Tick == StartTick || ModeRules.JoinsMidMatch(Mode));

    /// <summary>A team's kills, including those of players who have since left (two-team modes).</summary>
    public int TeamScore(byte team) => team < _teamScore.Length ? _teamScore[team] : 0;

    public int AliveCount(byte team = NoTeam)
    {
        int n = 0;
        foreach (ref readonly Standing p in _players.AsSpan()) if (p.Active && p.Alive && (team == NoTeam || p.Team == team)) n++;
        return n;
    }

    /// <summary>
    /// Puts <paramref name="slot"/>'s tank into the battle on a free spawn point of <paramref name="team"/> (ignored in
    /// a free-for-all or a royale, where each player is their own team), under protection.
    /// </summary>
    /// <returns>The tank's id.</returns>
    public int Join(byte slot, TankSpec spec, byte team)
    {
        if (!AcceptsJoins) throw new InvalidOperationException("The match takes no more players now.");
        if (_players[slot].Active) throw new InvalidOperationException($"Slot {slot} is already in the match.");
        if (ModeRules.HasTeams(Mode) && team > 1) throw new ArgumentOutOfRangeException(nameof(team), team, "Teams are 0 and 1.");
        byte actual = ModeRules.HasTeams(Mode) ? team : slot;
        SpawnPoint spawn = PickSpawn(actual, except: 0);
        int id = _sim.AddTank(spec, spawn.Position, spawn.Yaw, actual);
        _sim.Protect(id, ModeRules.ProtectionTicks);
        _players[slot] = new Standing { Active = true, TankId = id, Team = actual, Alive = true, RespawnAt = -1, EliminatedAt = -1 };
        _health[slot] = _hitRoom[slot] = spec.MaxHealth;
        _healthBeforeFall[slot] = 0;
        _joinedSeats |= 1u << slot;
        return id;
    }

    /// <summary>Takes <paramref name="slot"/>'s tank out of the battle; in a no-respawn mode that counts as going out.</summary>
    public void Leave(byte slot)
    {
        ref Standing p = ref _players[slot];
        if (!p.Active) return;
        if (_sim.SlotOf(p.TankId) >= 0) _sim.RemoveTank(p.TankId);
        p.Active = false;
        p.Alive = false;
        p.RespawnAt = -1;
        if (!ModeRules.Respawns(Mode) && p.EliminatedAt < 0)
        {
            p.EliminatedAt = _sim.Tick;
            _healthBeforeFall[slot] = _health[slot];
        }
    }

    /// <summary>
    /// After each step: in a royale the zone hurts the tanks outside it on each whole second of the match; then this tick's
    /// kills (the zone's included, credited to no one) and damage are scored, base capture counted, the due respawned, and
    /// the result called when it is decided; last, each seat's health is noted for the next tick's damage. The room sees
    /// to it that a match starts with at least two players, one per team: with fewer, a no-respawn match is decided on
    /// its first update (nobody, or only one side, left standing).
    /// </summary>
    public void Update()
    {
        if (Phase == MatchPhase.Ended) return;
        UpdateZone();
        HurtOutsideZone();
        foreach (ref readonly GameEvent e in _sim.Events)
        {
            if (e.Type == GameEventType.TankDestroyed) OnDestroyed(e);
            else if (e.Type is GameEventType.TankHit or GameEventType.SplashDamage) OnHurt(e);
        }
        Capture?.Update(_sim.Tanks, _sim.Events);
        if (ModeRules.Respawns(Mode)) RespawnDue();
        Judge();
        NoteHealth();
    }

    /// <summary><see cref="Zone"/> and <see cref="KnownRings"/> at the current tick (nothing without a zone).</summary>
    private void UpdateZone()
    {
        if (_zoneRings.Length == 0) return;
        int elapsed = _sim.Tick - StartTick;
        Zone = ZoneState.At(_zoneRings, elapsed);
        KnownRings = 1 + ZoneSchedule.AnnouncedBy(elapsed);
    }

    /// <summary>
    /// On each whole second since the start, every live tank outside the safe circle loses the zone's share of its max health. Each
    /// second crossed since the last update is hurt on once, with the zone as it was on that second (normally just this tick's):
    /// an update skipped over a second still costs it, where the tanks are now.
    /// </summary>
    private void HurtOutsideZone()
    {
        if (_zoneRings.Length == 0) return;
        int second = (_sim.Tick - StartTick) / SimConstants.TickRate;
        while (_lastZoneSecond < second)
        {
            _lastZoneSecond++;
            int tick = _lastZoneSecond * SimConstants.TickRate;
            HurtOutside(tick == _sim.Tick - StartTick ? Zone : ZoneState.At(_zoneRings, tick));
        }
    }

    private void HurtOutside(in ZoneState zone)
    {
        if (zone.DamagePercent <= 0) return;
        ReadOnlySpan<TankState> tanks = _sim.Tanks;   // HurtByZone changes health, never the order
        for (int i = 0; i < tanks.Length; i++)
        {
            ref readonly TankState t = ref tanks[i];
            if (!t.IsDestroyed && zone.IsOutside(t.Position))
                _sim.HurtByZone(t.Id, ZoneSchedule.DamageOf(t.Spec.MaxHealth, zone.DamagePercent));
        }
    }

    /// <summary>
    /// A hit on another side's tank counts as damage dealt by the shooter, up to the health the target had left (its own
    /// shells, team-mates and the zone count for nothing).
    /// </summary>
    private void OnHurt(in GameEvent e)
    {
        if (e.Damage <= 0) return;
        int shooter = SlotOfTank(e.TankId), target = SlotOfTank(e.TargetTankId);
        if (shooter < 0 || target < 0 || shooter == target || _players[shooter].Team == _players[target].Team) return;
        int dealt = Math.Min(e.Damage, _hitRoom[target]);
        _hitRoom[target] -= dealt;
        _players[shooter].Damage += dealt;
    }

    /// <summary>Each seat's tank's health now (0 for none), the cap on what the next tick's hits count.</summary>
    private void NoteHealth()
    {
        for (int slot = 0; slot < _players.Length; slot++)
        {
            int index = _players[slot].TankId == 0 ? -1 : _sim.SlotOf(_players[slot].TankId);
            _health[slot] = _hitRoom[slot] = index < 0 ? 0 : _sim.Tanks[index].Health;
        }
    }

    private void OnDestroyed(in GameEvent e)
    {
        int victim = SlotOfTank(e.TargetTankId), killer = SlotOfTank(e.TankId);
        if (victim < 0) return;
        ref Standing v = ref _players[victim];
        v.Deaths++;
        v.Alive = false;
        if (killer >= 0 && killer != victim && _players[killer].Team != v.Team)
        {
            _players[killer].Kills++;
            if (ModeRules.HasTeams(Mode)) _teamScore[_players[killer].Team]++;
        }
        if (ModeRules.Respawns(Mode)) v.RespawnAt = _sim.Tick + ModeRules.RespawnTicks;
        else
        {
            v.EliminatedAt = _sim.Tick;
            _healthBeforeFall[victim] = _health[victim];   // as the last update left it, before this tick's shells and zone
        }
    }

    private void RespawnDue()
    {
        for (byte slot = 0; slot < _players.Length; slot++)
        {
            ref Standing p = ref _players[slot];
            if (!p.Active || p.Alive || p.RespawnAt < 0 || _sim.Tick < p.RespawnAt) continue;
            SpawnPoint spawn = PickSpawn(p.Team, except: p.TankId);   // its own wreck doesn't block its point
            _sim.Respawn(p.TankId, spawn.Position, spawn.Yaw);
            _sim.Protect(p.TankId, ModeRules.ProtectionTicks);
            p.Alive = true;
            p.RespawnAt = -1;
        }
    }

    private void Judge()
    {
        bool timeUp = _sim.Tick >= EndTick;
        switch (Mode)
        {
            case BattleMode.TeamBattle when timeUp:
                End(Leader(), MatchEndReason.TimeUp);
                break;
            case BattleMode.Annihilation:
                int a = AliveCount(0), b = AliveCount(1);
                if (a == 0 || b == 0) End(a > 0 ? (byte)0 : b > 0 ? (byte)1 : NoTeam, MatchEndReason.Eliminated);
                else if (timeUp) End(NoTeam, MatchEndReason.TimeUp);
                break;
            case BattleMode.FreeForAll or BattleMode.Royale:
                if (AliveCount() <= 1) EndSolo(MatchEndReason.Eliminated);
                else if (timeUp) EndSolo(MatchEndReason.TimeUp);
                break;
            case BattleMode.Standard:
                JudgeStandard(timeUp);
                break;
        }
    }

    /// <summary>
    /// A team wins by wiping the other out or taking its base; both teams winning on one tick is a draw. A team can only
    /// take a base with its own tanks alive (a destroyed tank's points are gone), so one team's capture and its own
    /// wipe-out never come on the same tick: a draw is either both wiped out or both bases fallen, never one of each.
    /// A win both ways at once counts as a capture.
    /// </summary>
    private void JudgeStandard(bool timeUp)
    {
        BaseCapture capture = Capture!;
        bool took1 = capture.IsCaptured(1), took0 = capture.IsCaptured(0);   // team 0 took team 1's base; team 1 took team 0's
        bool won0 = AliveCount(1) == 0 || took1, won1 = AliveCount(0) == 0 || took0;
        if (won0 || won1)
        {
            byte team = won0 && won1 ? NoTeam : won0 ? (byte)0 : (byte)1;
            bool captured = (won0 && took1) || (won1 && took0);
            End(team, captured ? MatchEndReason.Captured : MatchEndReason.Eliminated);
        }
        else if (timeUp) End(NoTeam, MatchEndReason.TimeUp);
    }

    private byte Leader()
    {
        int a = TeamScore(0), b = TeamScore(1);
        return a == b ? NoTeam : a > b ? (byte)0 : (byte)1;
    }

    private void End(byte team, MatchEndReason reason)
    {
        WinnerTeam = team;
        Finish(reason);
    }

    private void Finish(MatchEndReason reason)
    {
        EndReason = reason;
        Phase = MatchPhase.Ended;
        EndedAt = _sim.Tick;
    }

    /// <summary>
    /// Ranks a free-for-all or a royale: the living by health, best first (the last one left, or whoever has the most when time is
    /// up, wins), then the fallen by how long they lasted, those who fell on one tick by the health they had before it (stage 6
    /// design §2.3: once the zone has closed they often go together); ties by kills, then by slot.
    /// </summary>
    private void EndSolo(MatchEndReason reason)
    {
        int n = 0;
        for (byte slot = 0; slot < _players.Length; slot++)
            if (_players[slot].Active || _players[slot].EliminatedAt >= 0) _rankScratch[n++] = slot;
        Span<byte> order = _rankScratch.AsSpan(0, n);
        // Insertion sort (stable, so slot order breaks the last ties) by (alive first, then health desc, then eliminated later first,
        // then health before the fall desc, then kills desc): n ≤ 16.
        for (int i = 1; i < n; i++)
        {
            byte key = order[i];
            int j = i - 1;
            while (j >= 0 && Before(key, order[j])) { order[j + 1] = order[j]; j--; }
            order[j + 1] = key;
        }
        for (int i = 0; i < n; i++) _players[order[i]].Rank = (byte)(i + 1);
        WinnerSlot = n > 0 ? order[0] : NoSlot;
        Finish(reason);
    }

    /// <summary>Whether <paramref name="x"/> places ahead of <paramref name="y"/>.</summary>
    private bool Before(byte x, byte y)
    {
        ref readonly Standing a = ref _players[x];
        ref readonly Standing b = ref _players[y];
        if (a.Alive != b.Alive) return a.Alive;
        if (a.Alive && HealthOf(a) != HealthOf(b)) return HealthOf(a) > HealthOf(b);
        if (!a.Alive && a.EliminatedAt != b.EliminatedAt) return a.EliminatedAt > b.EliminatedAt;
        if (!a.Alive && _healthBeforeFall[x] != _healthBeforeFall[y]) return _healthBeforeFall[x] > _healthBeforeFall[y];
        return a.Kills > b.Kills;
    }

    private int HealthOf(in Standing p)
    {
        int slot = _sim.SlotOf(p.TankId);
        return slot < 0 ? 0 : _sim.Tanks[slot].Health;
    }

    /// <summary>
    /// The first spawn point of <paramref name="team"/> (any, in a free-for-all or a royale; a royale tries them in its
    /// seeded order) with no tank or wreck on it; when all are taken, the roomiest one, shunted to the side it has most
    /// room on, so a tank is never put on top of another.
    /// </summary>
    /// <param name="except">A tank that doesn't count as occupying (the one about to respawn), or 0.</param>
    private SpawnPoint PickSpawn(byte team, int except)
    {
        IReadOnlyList<SpawnPoint> points = ModeRules.HasTeams(Mode) ? _spawns.Team : _spawns.Solo;
        SpawnPoint best = default;
        float bestRoom = -1f;
        for (int i = 0; i < points.Count; i++)   // indexed: a foreach over the interface would box an enumerator
        {
            SpawnPoint p = points[_spawnOrder?[i] ?? i];
            if (ModeRules.HasTeams(Mode) && p.Team != team) continue;
            float room = RoomAround(p.Position, except);
            if (room >= SpawnBusyRadius) return p;
            if (room > bestRoom) (best, bestRoom) = (p, room);
        }
        if (bestRoom < 0) throw new InvalidOperationException($"No spawn points for team {team}.");
        Vector2 side = new(MathF.Cos(best.Yaw), -MathF.Sin(best.Yaw));   // to the right of the heading (yaw 0 faces −Z)
        Vector2 at = best.Position;
        for (int k = -SpawnShunts; k <= SpawnShunts; k++)
        {
            Vector2 candidate = best.Position + side * (k * SpawnShunt);
            float room = RoomAround(candidate, except);
            if (room > bestRoom) (at, bestRoom) = (candidate, room);
        }
        return best with { Position = at };
    }

    /// <summary>How far the nearest tank or wreck (other than <paramref name="except"/>) is from <paramref name="point"/>.</summary>
    private float RoomAround(Vector2 point, int except)
    {
        float nearest = float.MaxValue;
        foreach (ref readonly TankState t in _sim.Tanks)
            if (t.Id != except) nearest = MathF.Min(nearest, Vector2.Distance(t.Position, point));   // wrecks count: a tank may not be put on one
        return nearest;
    }

    /// <summary>The slot whose tank is <paramref name="tankId"/>, left or not (ids are never reused, so a stale one is still its player's).</summary>
    private int SlotOfTank(int tankId)
    {
        if (tankId == 0) return -1;
        for (int i = 0; i < _players.Length; i++)
            if (_players[i].TankId == tankId) return i;
        return -1;
    }
}
