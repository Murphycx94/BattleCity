using System.Numerics;
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
    public byte Rank;          // final place in a free-for-all, 1 best; 0 until the match ends
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

    private readonly Simulation _sim;
    private readonly SpawnLayout _spawns;
    private readonly Standing[] _players = new Standing[ModeRules.MaxPlayers];
    private readonly byte[] _rankScratch = new byte[ModeRules.MaxPlayers];
    private readonly int[] _teamScore = new int[2];   // kills by team, kept when a player leaves
    private readonly int _startTick;

    /// <param name="durationTicks">A match length other than the mode's (tests), positive; null for the mode's own.</param>
    /// <param name="bases">The map's bases: required (<see cref="TeamBase.Check"/>, not empty) in a standard match, ignored in the others.</param>
    public MatchRules(BattleMode mode, Simulation sim, SpawnLayout spawns, int? durationTicks = null, IReadOnlyList<TeamBase>? bases = null)
    {
        if (durationTicks is <= 0) throw new ArgumentOutOfRangeException(nameof(durationTicks), durationTicks, "A match lasts at least one tick.");
        if (ModeRules.HasBases(mode)) Capture = new BaseCapture(bases ?? Array.Empty<TeamBase>());   // checks them: ArgumentException, param "bases"
        (Mode, _sim, _spawns) = (mode, sim, spawns);
        _startTick = sim.Tick;
        EndTick = sim.Tick + (durationTicks ?? ModeRules.DurationTicks(mode));
        for (int i = 0; i < _players.Length; i++) _players[i] = new Standing { RespawnAt = -1, EliminatedAt = -1 };
    }

    public BattleMode Mode { get; }
    public MatchPhase Phase { get; private set; } = MatchPhase.Playing;

    /// <summary>The tick the clock runs out.</summary>
    public int EndTick { get; }

    /// <summary>The tick the match ended, or -1 while it is on.</summary>
    public int EndedAt { get; private set; } = -1;

    public int TicksLeft => Math.Max(0, EndTick - _sim.Tick);

    /// <summary>The winning team (two-team modes), or <see cref="NoTeam"/> for a draw or while the match is on.</summary>
    public byte WinnerTeam { get; private set; } = NoTeam;

    /// <summary>The winning player's slot (free-for-all), or <see cref="NoSlot"/>.</summary>
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

    /// <summary>Whether another player may be put into the match now.</summary>
    public bool AcceptsJoins => Phase == MatchPhase.Playing && (_sim.Tick == _startTick || ModeRules.JoinsMidMatch(Mode));

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
    /// a free-for-all, where each player is their own team), under protection.
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
        if (!ModeRules.Respawns(Mode) && p.EliminatedAt < 0) p.EliminatedAt = _sim.Tick;
    }

    /// <summary>
    /// After each step: scores this tick's kills, counts base capture, respawns the due, and calls the result when it is
    /// decided. The room sees to it that a match starts with at least two players, one per team: with fewer, a
    /// no-respawn match is decided on its first update (nobody, or only one side, left standing).
    /// </summary>
    public void Update()
    {
        if (Phase == MatchPhase.Ended) return;
        foreach (ref readonly GameEvent e in _sim.Events)
            if (e.Type == GameEventType.TankDestroyed) OnDestroyed(e);
        Capture?.Update(_sim.Tanks, _sim.Events);
        if (ModeRules.Respawns(Mode)) RespawnDue();
        Judge();
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
        else v.EliminatedAt = _sim.Tick;
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
            case BattleMode.FreeForAll:
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
    /// Ranks a free-for-all: the living by health, best first (the last one left, or whoever has the most when time is
    /// up, wins), then the fallen by how long they lasted; ties by kills, then by slot.
    /// </summary>
    private void EndSolo(MatchEndReason reason)
    {
        int n = 0;
        for (byte slot = 0; slot < _players.Length; slot++)
            if (_players[slot].Active || _players[slot].EliminatedAt >= 0) _rankScratch[n++] = slot;
        Span<byte> order = _rankScratch.AsSpan(0, n);
        // Insertion sort by (alive first, then health desc, then eliminated later first, then kills desc): n ≤ 10.
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
        return a.Kills > b.Kills;
    }

    private int HealthOf(in Standing p)
    {
        int slot = _sim.SlotOf(p.TankId);
        return slot < 0 ? 0 : _sim.Tanks[slot].Health;
    }

    /// <summary>
    /// The first spawn point of <paramref name="team"/> (any, in a free-for-all) with no tank or wreck on it; when all
    /// are taken, the roomiest one, shunted to the side it has most room on, so a tank is never put on top of another.
    /// </summary>
    /// <param name="except">A tank that doesn't count as occupying (the one about to respawn), or 0.</param>
    private SpawnPoint PickSpawn(byte team, int except)
    {
        IReadOnlyList<SpawnPoint> points = ModeRules.HasTeams(Mode) ? _spawns.Team : _spawns.Solo;
        SpawnPoint best = default;
        float bestRoom = -1f;
        for (int i = 0; i < points.Count; i++)   // indexed: a foreach over the interface would box an enumerator
        {
            SpawnPoint p = points[i];
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
