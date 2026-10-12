using Tank.Core.Math;
using Tank.Core.World;

namespace Tank.Core.Systems;

internal static class BulletSystem
{
    public static void Run(Simulation sim)
    {
        var s = sim.State;
        for (int i = 0; i < s.BulletCount; i++)
        {
            int remaining = s.Bullets[i].Speed;
            while (remaining > 0 && !s.Bullets[i].Dead)
            {
                int step = System.Math.Min(remaining, Units.BulletSubstep);
                remaining -= step;
                ref var b = ref s.Bullets[i];
                b.X += b.Dir.Dx() * step;
                b.Z += b.Dir.Dz() * step;
                ResolveCollisions(sim, i);
            }
        }
    }

    private static void ResolveCollisions(Simulation sim, int bulletIndex)
    {
        var s = sim.State;
        ref var b = ref s.Bullets[bulletIndex];
        Box box = b.Box;

        if (BulletPhysics.OutOfBounds(s.Map, box))
        {
            Kill(sim, ref b);
            return;
        }
        if (BulletPhysics.HitsTerrain(s.Map, box))
        {
            BulletPhysics.DamageTerrain(s.Map, b);
            Kill(sim, ref b);
            return;
        }
        if (HitEagle(sim, ref b, box)) return;
        if (HitTank(sim, ref b, box)) return;
        HitBullet(sim, bulletIndex, box);
    }

    private static bool HitEagle(Simulation sim, ref BulletState b, in Box box)
    {
        var s = sim.State;
        for (int e = 0; e < s.EagleCount; e++)
        {
            ref var eagle = ref s.Eagles[e];
            if (!eagle.Alive || !eagle.Box.Overlaps(box)) continue;
            eagle.Alive = false;
            b.Dead = true;
            sim.Emit(GameEventType.EagleDestroyed, eagle.X + Units.Tile / 2, eagle.Z + Units.Tile / 2, eagle.Team);
            return true;
        }
        return false;
    }

    private static bool HitTank(Simulation sim, ref BulletState b, in Box box)
    {
        var s = sim.State;
        for (int t = 0; t < s.TankCount; t++)
        {
            ref var tank = ref s.Tanks[t];
            if (tank.Dead || tank.Team == b.Team || tank.SpawnTicks > 0) continue;
            if (!TankBoxForBullet(sim, b, tank).Overlaps(box)) continue;
            b.Dead = true;
            CombatSystem.ApplyHit(sim, t, b.OwnerSlot);
            return true;
        }
        return false;
    }

    /// <summary>Lag compensation: compare against where the shooter saw the tank.</summary>
    private static Box TankBoxForBullet(Simulation sim, in BulletState b, in TankState tank)
    {
        if (b.LagTicks == 0) return tank.Box;
        return sim.History.TryGetBox(tank.Id, sim.State.Tick - b.LagTicks, out Box past) ? past : tank.Box;
    }

    private static void HitBullet(Simulation sim, int bulletIndex, in Box box)
    {
        var s = sim.State;
        ref var b = ref s.Bullets[bulletIndex];
        for (int j = 0; j < s.BulletCount; j++)
        {
            if (j == bulletIndex) continue;
            ref var other = ref s.Bullets[j];
            if (other.Dead || other.Team == b.Team || !other.Box.Overlaps(box)) continue;
            other.Dead = true;
            Kill(sim, ref b);
            return;
        }
    }

    private static void Kill(Simulation sim, ref BulletState b)
    {
        b.Dead = true;
        sim.Emit(GameEventType.BulletSpark, b.X, b.Z, (int)b.Dir, b.OwnerSlot);
    }
}
