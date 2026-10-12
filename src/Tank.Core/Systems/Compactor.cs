using Tank.Core.World;

namespace Tank.Core.Systems;

/// <summary>Removes dead entities while preserving id order (keeps iteration deterministic).</summary>
internal static class Compactor
{
    public static void Run(GameState s)
    {
        s.TankCount = CompactTanks(s.Tanks, s.TankCount);
        s.BulletCount = Compact(s.Bullets, s.BulletCount, static (in BulletState b) => b.Dead);
        s.PowerUpCount = Compact(s.PowerUps, s.PowerUpCount, static (in PowerUpState p) => p.Dead);
    }

    public static int CompactTanks(TankState[] tanks, int count) =>
        Compact(tanks, count, static (in TankState t) => t.Dead);

    private delegate bool IsDead<T>(in T item);

    private static int Compact<T>(T[] items, int count, IsDead<T> isDead)
    {
        int w = 0;
        for (int r = 0; r < count; r++)
        {
            if (isDead(in items[r])) continue;
            if (w != r) items[w] = items[r];
            w++;
        }
        for (int i = w; i < count; i++) items[i] = default!;
        return w;
    }
}
