namespace Tank.Sim.Modes;

/// <summary>Duos' result (stage 6 design §2.3, §5): judged and ranked by team.</summary>
public sealed partial class MatchRules
{
    /// <summary>The joined teams in the order they place, while <see cref="EndDuos"/> ranks them.</summary>
    private readonly byte[] _teamOrder = new byte[ModeRules.DuoTeams];

    /// <summary>Per team, as <see cref="EndDuos"/> totals it: its live tanks, their health, its kills.</summary>
    private readonly int[] _teamAlive = new int[ModeRules.DuoTeams];
    private readonly int[] _teamHealth = new int[ModeRules.DuoTeams];
    private readonly int[] _teamKills = new int[ModeRules.DuoTeams];

    /// <summary>Per team with none alive: the tick its last tank went out (or left), and what those out on that tick had going into it.</summary>
    private readonly int[] _teamOutAt = new int[ModeRules.DuoTeams];
    private readonly int[] _teamFallHealth = new int[ModeRules.DuoTeams];

    /// <summary>
    /// Ranks the teams that joined (a team whose two both left among them): those with a live tank first, by their living's health,
    /// most first (the last team left, or the healthiest when time is up, wins); then those out, by the tick their last tank went out,
    /// later first, those out on one tick by the health those last tanks had going into it (stage 6 design §2.3, as a royale's);
    /// ties by the team's kills, then by team number. Both seats of a team take its place. Runs once, on the ending tick, on
    /// preallocated scratch: no allocation.
    /// </summary>
    private void EndDuos(MatchEndReason reason)
    {
        Array.Clear(_teamAlive);
        Array.Clear(_teamHealth);
        Array.Clear(_teamKills);
        Array.Fill(_teamOutAt, -1);
        Array.Clear(_teamFallHealth);
        for (int slot = 0; slot < _players.Length; slot++)
        {
            ref readonly Standing p = ref _players[slot];
            if (!p.Active && p.EliminatedAt < 0) continue;   // never joined
            int team = p.Team;
            _teamKills[team] += p.Kills;
            if (p.Active && p.Alive)
            {
                _teamAlive[team]++;
                _teamHealth[team] += HealthOf(p);
            }
            else if (p.EliminatedAt > _teamOutAt[team]) (_teamOutAt[team], _teamFallHealth[team]) = (p.EliminatedAt, _healthBeforeFall[slot]);
            else if (p.EliminatedAt == _teamOutAt[team]) _teamFallHealth[team] += _healthBeforeFall[slot];
        }

        int n = 0;
        for (byte team = 0; team < ModeRules.DuoTeams; team++)
            if ((_joinedTeams & (1u << team)) != 0) _teamOrder[n++] = team;
        Span<byte> order = _teamOrder.AsSpan(0, n);
        // Insertion sort, stable so team order breaks the last ties: n ≤ 8.
        for (int i = 1; i < n; i++)
        {
            byte key = order[i];
            int j = i - 1;
            while (j >= 0 && TeamBefore(key, order[j])) { order[j + 1] = order[j]; j--; }
            order[j + 1] = key;
        }

        for (int slot = 0; slot < _players.Length; slot++)
        {
            ref Standing p = ref _players[slot];
            if (!p.Active && p.EliminatedAt < 0) continue;
            p.Rank = (byte)(order.IndexOf(p.Team) + 1);
        }
        WinnerTeam = n > 0 ? order[0] : NoTeam;
        WinnerSlot = NoSlot;
        Finish(reason);
    }

    /// <summary>Whether team <paramref name="x"/> places ahead of team <paramref name="y"/> (the totals <see cref="EndDuos"/> made).</summary>
    private bool TeamBefore(byte x, byte y)
    {
        bool xIn = _teamAlive[x] > 0, yIn = _teamAlive[y] > 0;
        if (xIn != yIn) return xIn;
        if (xIn && _teamHealth[x] != _teamHealth[y]) return _teamHealth[x] > _teamHealth[y];
        if (!xIn && _teamOutAt[x] != _teamOutAt[y]) return _teamOutAt[x] > _teamOutAt[y];
        if (!xIn && _teamFallHealth[x] != _teamFallHealth[y]) return _teamFallHealth[x] > _teamFallHealth[y];
        return _teamKills[x] > _teamKills[y];
    }
}
