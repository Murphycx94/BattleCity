using Tank.Core.Rules;
using Tank.Core.World;

namespace Tank.Core.Systems;

internal static class CombatSystem
{
    public static void ApplyHit(Simulation sim, int tankIndex, byte shooterSlot)
    {
        var s = sim.State;
        ref var tank = ref s.Tanks[tankIndex];

        if (tank.ShieldTicks > 0)
        {
            sim.Emit(GameEventType.ShieldBlocked, tank.CenterX, tank.CenterZ);
            return;
        }
        if (tank.Bonus)
        {
            tank.Bonus = false;
            PowerUpSystem.SpawnRandom(sim);
        }
        if (tank.Hp > 1)
        {
            tank.Hp--;
            sim.Emit(GameEventType.TankHit, tank.CenterX, tank.CenterZ, tank.Hp);
            return;
        }
        Destroy(sim, tankIndex, shooterSlot, awardScore: true);
    }

    public static void Destroy(Simulation sim, int tankIndex, byte killerSlot, bool awardScore)
    {
        var s = sim.State;
        ref var tank = ref s.Tanks[tankIndex];
        tank.Dead = true;
        sim.Emit(GameEventType.TankDestroyed, tank.CenterX, tank.CenterZ, (int)tank.Kind, killerSlot);

        if (awardScore && killerSlot < GameState.MaxPlayers && killerSlot != tank.Slot)
        {
            ref var killer = ref s.Players[killerSlot];
            killer.Score += TankStats.KillScore(tank.Kind);
            killer.Kills++;
        }

        if (!tank.IsPlayer || tank.Slot >= GameState.MaxPlayers) return;
        ref var victim = ref s.Players[tank.Slot];
        victim.TankId = 0;
        victim.Deaths++;
        victim.Stars = 0;
        victim.RespawnTicks = TankStats.RespawnDelayTicks;
        if (s.Config.Mode == GameMode.Coop)
        {
            if (victim.Lives > 0) victim.Lives--;
            if (victim.Lives == 0) victim.Eliminated = true;
        }
    }
}
