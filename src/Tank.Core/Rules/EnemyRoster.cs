using Tank.Core.Math;
using Tank.Core.World;

namespace Tank.Core.Rules;

/// <summary>Deterministic enemy line-up per co-op stage: harder stages bring more fast/power/armor tanks.</summary>
public static class EnemyRoster
{
    private static readonly int[] BonusIndices = { 3, 10, 17 };

    public static TankKind[] ForStage(int stage, int total)
    {
        if (total <= 0) return Array.Empty<TankKind>();
        stage = System.Math.Max(1, stage);
        int armor = System.Math.Min(1 + stage / 3, total / 3);
        int power = System.Math.Min(1 + stage / 4, total / 4);
        int fast = System.Math.Min(2 + stage / 2, total / 3);
        int basic = System.Math.Max(0, total - armor - power - fast);

        var kinds = new List<TankKind>(total);
        kinds.AddRange(Enumerable.Repeat(TankKind.Basic, basic));
        kinds.AddRange(Enumerable.Repeat(TankKind.Fast, fast));
        kinds.AddRange(Enumerable.Repeat(TankKind.Power, power));
        kinds.AddRange(Enumerable.Repeat(TankKind.Armor, armor));

        // Easier tanks first on average, but shuffled deterministically per stage.
        var rng = new XorShift((uint)(stage * 7919 + 17));
        var result = kinds.Take(total).ToArray();
        for (int i = result.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
    }

    public static bool IsBonus(int spawnIndex) => Array.IndexOf(BonusIndices, spawnIndex) >= 0;
}
