using Tank.Core.World;

namespace Tank.Core.Systems;

internal static class TimerSystem
{
    public static void Run(Simulation sim)
    {
        var s = sim.State;
        for (int i = 0; i < s.TankCount; i++)
        {
            ref var t = ref s.Tanks[i];
            if (t.ShieldTicks > 0) t.ShieldTicks--;
            if (t.SpawnTicks > 0) t.SpawnTicks--;
            if (t.FireCooldown > 0) t.FireCooldown--;
        }
        for (int team = 0; team < Teams.Count; team++)
            if (s.FreezeTicks[team] > 0) s.FreezeTicks[team]--;
        for (int e = 0; e < s.EagleCount; e++)
        {
            if (s.ShovelTicks[e] == 0) continue;
            s.ShovelTicks[e]--;
            if (s.ShovelTicks[e] == 0) PowerUpSystem.Fortify(s, e, World.CellType.Brick);
        }
        if (s.Config.Mode == GameMode.Deathmatch && s.TicksRemaining > 0) s.TicksRemaining--;
    }
}
