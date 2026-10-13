using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;

namespace Tank.Sim.Maps;

/// <summary>400m × 400m test ground: rolling noise, four hills, scattered rocks. The spawn faces the big hill (north).</summary>
public static class SandboxMap
{
    public const int Seed = 20261013;
    public const int Samples = 201;
    public const float CellSize = 2f;
    public const float Border = 10f;
    private const int RockAttempts = 48;
    private const float SpawnClearance = 30f;

    public static readonly Vector2 Spawn = new(200, 330);

    private static readonly TerrainGenerator.Hill[] Hills =
    {
        new(200, 150, 70, 22),
        new(90, 250, 45, 12),
        new(310, 230, 55, 16),
        new(140, 90, 35, 9),
    };

    public static Battlefield Create()
    {
        HeightField terrain = TerrainGenerator.Generate(Seed, Samples, CellSize, roughness: 4f, Hills);
        return new Battlefield(terrain, ScatterRocks(terrain), Border);
    }

    private static List<Prop> ScatterRocks(HeightField terrain)
    {
        var rng = new XorShift(Seed);
        var rocks = new List<Prop>();
        for (int k = 0; k < RockAttempts; k++)
        {
            var center = new Vector2(40 + rng.NextFloat() * 320, 40 + rng.NextFloat() * 320);
            var half = new Vector2(1 + rng.NextFloat() * 2, 1 + rng.NextFloat() * 2.5f);
            float height = 1.5f + rng.NextFloat() * 3f;
            float yaw = rng.NextFloat() * Angles.TwoPi;
            if (Vector2.Distance(center, Spawn) < SpawnClearance) continue;
            float ground = terrain.HeightAt(center.X, center.Y);
            // Sink the base 1m so the rock never floats on a slope.
            rocks.Add(new Prop(rocks.Count + 1, PropKind.Rock, new Obb2(center, half, yaw), ground - 1f, height + 1f));
        }
        return rocks;
    }
}
