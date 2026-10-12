using Tank.Core.Math;
using Tank.Core.World;

namespace Tank.Core.AI;

/// <summary>
/// NES-flavoured enemy brain: wander, re-roll direction when blocked or on a timer, drift toward the
/// eagle or the nearest player, fire at random. Uses only the seeded state RNG.
/// </summary>
internal static class EnemyAi
{
    public static InputCmd Think(Simulation sim, ref TankState tank)
    {
        var s = sim.State;
        if (tank.SpawnTicks > 0 || s.FreezeTicks[tank.Team] > 0) return InputCmd.Idle(s.Tick);

        if (tank.AiTimer > 0) tank.AiTimer--;
        if (tank.Blocked || tank.AiTimer == 0)
        {
            tank.Dir = ChooseDirection(s, tank);
            tank.AiTimer = (ushort)(30 + s.Rng.Next(90));
        }

        int fireDen = tank.Kind == TankKind.Power ? 12 : 24;
        bool fire = s.Rng.Chance(1, fireDen);
        return new InputCmd(s.Tick, InputCmd.Encode(tank.Dir), fire);
    }

    private static Dir ChooseDirection(GameState s, in TankState tank)
    {
        int roll = s.Rng.Next(8);
        if (roll < 3) return (Dir)s.Rng.Next(4);
        if (roll < 6 && TryEagle(s, out int ex, out int ez)) return Toward(s, tank, ex, ez);
        if (TryNearestPlayer(s, tank, out int px, out int pz)) return Toward(s, tank, px, pz);
        return Dir.Down;
    }

    private static Dir Toward(GameState s, in TankState tank, int tx, int tz)
    {
        int dx = tx - tank.CenterX, dz = tz - tank.CenterZ;
        bool horizontal = dx != 0 && (dz == 0 || s.Rng.Chance(1, 2));
        if (horizontal) return dx > 0 ? Dir.Right : Dir.Left;
        return dz > 0 ? Dir.Down : Dir.Up;
    }

    private static bool TryEagle(GameState s, out int x, out int z)
    {
        for (int e = 0; e < s.EagleCount; e++)
        {
            if (!s.Eagles[e].Alive) continue;
            x = s.Eagles[e].X + Units.Tile / 2;
            z = s.Eagles[e].Z + Units.Tile / 2;
            return true;
        }
        x = z = 0;
        return false;
    }

    private static bool TryNearestPlayer(GameState s, in TankState self, out int x, out int z)
    {
        long best = long.MaxValue;
        x = z = 0;
        for (int i = 0; i < s.TankCount; i++)
        {
            ref readonly var t = ref s.Tanks[i];
            if (t.Dead || !t.IsPlayer) continue;
            long dx = t.CenterX - self.CenterX, dz = t.CenterZ - self.CenterZ;
            long d = dx * dx + dz * dz;
            if (d >= best) continue;
            best = d;
            x = t.CenterX;
            z = t.CenterZ;
        }
        return best != long.MaxValue;
    }
}
