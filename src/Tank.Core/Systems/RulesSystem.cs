using Tank.Core.World;

namespace Tank.Core.Systems;

internal static class RulesSystem
{
    public static void Run(Simulation sim)
    {
        var s = sim.State;
        switch (s.Config.Mode)
        {
            case GameMode.Coop: Coop(sim); break;
            case GameMode.BaseBattle: BaseBattle(sim); break;
            case GameMode.Deathmatch: Deathmatch(sim); break;
        }
    }

    private static void Coop(Simulation sim)
    {
        var s = sim.State;
        if (AnyEagleDead(s, out _))
        {
            End(sim, Teams.Enemy);
            return;
        }
        int active = 0, eliminated = 0;
        for (int i = 0; i < GameState.MaxPlayers; i++)
        {
            if (!s.Players[i].Active) continue;
            active++;
            if (s.Players[i].Eliminated) eliminated++;
        }
        if (active > 0 && eliminated == active)
        {
            End(sim, Teams.Enemy);
            return;
        }
        if (s.EnemiesToSpawn == 0 && s.EnemiesSpawned > 0 && s.AliveEnemyCount() == 0) End(sim, 0);
    }

    private static void BaseBattle(Simulation sim)
    {
        if (AnyEagleDead(sim.State, out byte loser)) End(sim, loser == 0 ? (byte)1 : (byte)0);
    }

    private static void Deathmatch(Simulation sim)
    {
        var s = sim.State;
        int best = -1, bestKills = -1;
        for (int i = 0; i < GameState.MaxPlayers; i++)
        {
            if (!s.Players[i].Active || s.Players[i].Kills <= bestKills) continue;
            best = i;
            bestKills = s.Players[i].Kills;
        }
        if (best < 0) return;
        if (s.TicksRemaining == 0 || bestKills >= s.Config.DeathmatchScoreLimit) End(sim, s.Players[best].Team);
    }

    private static bool AnyEagleDead(GameState s, out byte team)
    {
        for (int e = 0; e < s.EagleCount; e++)
        {
            if (s.Eagles[e].Alive) continue;
            team = s.Eagles[e].Team;
            return true;
        }
        team = Teams.None;
        return false;
    }

    private static void End(Simulation sim, byte winner)
    {
        var s = sim.State;
        s.Phase = MatchPhase.Ended;
        s.WinnerTeam = winner;
        sim.Emit(GameEventType.MatchEnded, 0, 0, winner);
    }
}
