using Tank.Core.Math;
using Tank.Core.Rules;
using Tank.Core.World;

namespace Tank.Core.Systems;

internal static class PowerUpSystem
{
    private const int PowerUpTypeCount = 6;

    public static void Run(Simulation sim)
    {
        var s = sim.State;
        for (int p = 0; p < s.PowerUpCount; p++)
        {
            ref var pu = ref s.PowerUps[p];
            if (pu.Dead) continue;
            if (pu.Ttl > 0 && --pu.Ttl == 0)
            {
                pu.Dead = true;
                continue;
            }
            for (int t = 0; t < s.TankCount; t++)
            {
                ref var tank = ref s.Tanks[t];
                if (tank.Dead || !tank.IsPlayer || !tank.Box.Overlaps(pu.Box)) continue;
                pu.Dead = true;
                sim.Emit(GameEventType.PowerUpTaken, pu.X + Units.Tile / 2, pu.Z + Units.Tile / 2, (int)pu.Type, tank.Slot);
                Apply(sim, t, pu.Type);
                break;
            }
        }
    }

    public static void SpawnRandom(Simulation sim)
    {
        var s = sim.State;
        if (s.PowerUpCount >= GameState.MaxPowerUps)
        {
            // Drop the oldest. Only the power-up array shifts; tank/bullet indices stay valid for callers.
            Array.Copy(s.PowerUps, 1, s.PowerUps, 0, s.PowerUpCount - 1);
            s.PowerUpCount--;
        }

        int maxHx = (s.Map.WidthSub - Units.Tile) / Units.Half;
        int maxHz = (s.Map.HeightSub - Units.Tile) / Units.Half;
        var type = (PowerUpType)s.Rng.Next(PowerUpTypeCount);
        int x = s.Rng.Next(maxHx + 1) * Units.Half;
        int z = s.Rng.Next(maxHz + 1) * Units.Half;
        s.PowerUps[s.PowerUpCount++] = new PowerUpState
        {
            Id = s.AllocateId(),
            Type = type,
            X = x,
            Z = z,
            Ttl = TankStats.PowerUpTtlTicks,
        };
        sim.Emit(GameEventType.PowerUpSpawned, x + Units.Tile / 2, z + Units.Tile / 2, (int)type);
    }

    private static void Apply(Simulation sim, int tankIndex, PowerUpType type)
    {
        var s = sim.State;
        ref var tank = ref s.Tanks[tankIndex];
        bool coop = s.Config.Mode == GameMode.Coop;
        switch (type)
        {
            case PowerUpType.Helmet:
                tank.ShieldTicks = TankStats.HelmetShieldTicks;
                break;
            case PowerUpType.Clock:
                for (int team = 0; team < Teams.Count; team++)
                    if (team != tank.Team)
                        s.FreezeTicks[team] = coop ? (ushort)TankStats.CoopFreezeTicks : (ushort)TankStats.VersusFreezeTicks;
                break;
            case PowerUpType.Shovel:
                for (int e = 0; e < s.EagleCount; e++)
                {
                    if (s.Eagles[e].Team != tank.Team || !s.Eagles[e].Alive) continue;
                    Fortify(s, e, CellType.Steel);
                    s.ShovelTicks[e] = TankStats.ShovelTicks;
                }
                break;
            case PowerUpType.Star:
                if (tank.Stars < TankStats.MaxStars) tank.Stars++;
                if (tank.Slot < GameState.MaxPlayers) s.Players[tank.Slot].Stars = tank.Stars;
                break;
            case PowerUpType.Grenade:
                byte team0 = tank.Team, slot = tank.Slot;
                for (int t = 0; t < s.TankCount; t++)
                {
                    ref var other = ref s.Tanks[t];
                    if (other.Dead || other.Team == team0 || other.ShieldTicks > 0 || other.SpawnTicks > 0) continue;
                    CombatSystem.Destroy(sim, t, slot, awardScore: !coop);
                }
                break;
            case PowerUpType.ExtraLife:
                if (tank.Slot < GameState.MaxPlayers) s.Players[tank.Slot].Lives++;
                break;
        }
    }

    /// <summary>Sets the half-tile ring around an eagle to <paramref name="type"/> (shovel power-up).</summary>
    public static void Fortify(GameState s, int eagleIndex, CellType type)
    {
        Box eagle = s.Eagles[eagleIndex].Box;
        int x0 = IntMath.FloorDiv(eagle.X - Units.Half, Units.Cell), x1 = IntMath.FloorDiv(eagle.MaxX + Units.Half - 1, Units.Cell);
        int z0 = IntMath.FloorDiv(eagle.Z - Units.Half, Units.Cell), z1 = IntMath.FloorDiv(eagle.MaxZ + Units.Half - 1, Units.Cell);
        for (int cz = z0; cz <= z1; cz++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                var cellBox = new Box(cx * Units.Cell, cz * Units.Cell, Units.Cell, Units.Cell);
                if (cellBox.Overlaps(eagle) || !s.Map.InBounds(cx, cz) || OccupiedByTank(s, cellBox)) continue;
                s.Map.Set(cx, cz, type);
            }
        }
    }

    private static bool OccupiedByTank(GameState s, in Box cell)
    {
        for (int t = 0; t < s.TankCount; t++)
            if (!s.Tanks[t].Dead && s.Tanks[t].Box.Overlaps(cell)) return true;
        return false;
    }
}
