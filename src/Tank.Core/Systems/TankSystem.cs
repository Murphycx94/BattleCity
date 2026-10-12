using Tank.Core.Rules;
using Tank.Core.World;

namespace Tank.Core.Systems;

internal static class TankSystem
{
    public const int MaxLagCompensationTicks = 12;

    public static void Run(Simulation sim, ReadOnlySpan<InputCmd> playerInputs)
    {
        var s = sim.State;
        for (int i = 0; i < s.TankCount; i++)
        {
            ref var tank = ref s.Tanks[i];
            if (tank.Dead) continue;

            InputCmd input = tank.IsPlayer
                ? (tank.Slot < playerInputs.Length ? playerInputs[tank.Slot] : InputCmd.Idle(s.Tick))
                : AI.EnemyAi.Think(sim, ref tank);

            bool frozen = s.FreezeTicks[tank.Team] > 0;
            TankMovement.Step(ref tank, input, frozen, s.Map, sim.ObstaclesExcept(tank.Id));
            if (input.Fire && !frozen) TryFire(sim, ref tank, input);
        }
    }

    public static bool CanFire(GameState s, in TankState tank) =>
        tank.SpawnTicks == 0 &&
        tank.FireCooldown == 0 &&
        s.BulletCount < GameState.MaxBullets &&
        s.ActiveBulletsOf(tank.Id) < TankStats.MaxBullets(tank);

    private static void TryFire(Simulation sim, ref TankState tank, in InputCmd input)
    {
        var s = sim.State;
        if (!CanFire(s, tank)) return;

        var (x, z) = BulletPhysics.MuzzlePosition(tank);
        s.Bullets[s.BulletCount++] = new BulletState
        {
            Id = s.AllocateId(),
            OwnerTankId = tank.Id,
            OwnerSlot = tank.Slot,
            Team = tank.Team,
            X = x,
            Z = z,
            Dir = tank.Dir,
            Speed = (byte)TankStats.BulletSpeed(tank),
            Power = TankStats.PowerBullet(tank),
            LagTicks = tank.IsPlayer ? (byte)System.Math.Min((int)input.ViewDelta, MaxLagCompensationTicks) : (byte)0,
            SpawnInputTick = tank.IsPlayer ? input.Tick : -1,
        };
        tank.FireCooldown = TankStats.FireCooldownTicks;
        sim.Emit(GameEventType.Fired, x, z, tank.Slot);
    }
}
