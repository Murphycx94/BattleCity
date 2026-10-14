using System.Numerics;
using Tank.Client.Art.Meshes;
using Tank.Sim.Maps;

namespace Tank.Art.Tests;

public class DirtPatchesTests
{
    private static readonly IReadOnlyList<Vector2> NoIslands = Array.Empty<Vector2>();

    private static DirtPatches Sandbox() => new(400, 400, SandboxMap.BushPatches, SandboxMap.Seed);

    [Fact]
    public void Same_seed_and_islands_give_identical_bytes()
    {
        Assert.Equal(Sandbox().ToBytes(), Sandbox().ToBytes());
    }

    [Fact]
    public void Different_seeds_differ()
    {
        Assert.NotEqual(new DirtPatches(400, 400, SandboxMap.BushPatches, 7).ToBytes(), new DirtPatches(400, 400, SandboxMap.BushPatches, 8).ToBytes());
    }

    [Fact]
    public void Lookups_stay_within_zero_and_one_and_most_of_the_map_is_dirt()
    {
        DirtPatches mask = Sandbox();
        int dirt = 0, total = 0;
        for (float x = -10; x <= 410; x += 4)
        for (float z = -10; z <= 410; z += 4)
        {
            float v = mask.At(x, z);
            Assert.InRange(v, 0f, 1f);
            if (v >= 0.5f) dirt++;
            total++;
        }
        Assert.InRange(dirt / (float)total, 0.6f, 0.8f);
    }

    [Fact]
    public void Every_bush_patch_centre_sits_on_a_grass_island()
    {
        DirtPatches mask = Sandbox();
        Assert.All(SandboxMap.BushPatches, p => Assert.True(mask.At(p.X, p.Y) < 0.1f, $"patch {p}: dirt {mask.At(p.X, p.Y):F2}"));
    }

    [Fact]
    public void Every_bush_grows_on_grass()
    {
        DirtPatches mask = Sandbox();
        Assert.All(SandboxMap.Create().Props.Where(p => p.Kind == PropKind.Bush), b =>
            Assert.True(mask.At(b.Footprint.Center.X, b.Footprint.Center.Y) < 0.5f, $"bush {b.Id} at {b.Footprint.Center} stands on dirt"));
    }

    [Fact]
    public void The_spawn_starts_on_bare_dirt()
    {
        DirtPatches mask = Sandbox();
        Assert.True(mask.At(SandboxMap.Spawn.X, SandboxMap.Spawn.Y) >= 0.9f, "the spawn should stand on dirt");
        int dirt = 0, total = 0;
        for (float x = -30; x <= 30; x += 2)
        for (float z = -30; z <= 30; z += 2)
        {
            if (x * x + z * z > 30 * 30) continue;
            total++;
            if (mask.At(SandboxMap.Spawn.X + x, SandboxMap.Spawn.Y + z) >= 0.5f) dirt++;
        }
        Assert.True(dirt >= 0.8f * total, $"only {dirt} of {total} points around the spawn are dirt");
    }

    [Fact]
    public void Some_grass_islands_come_from_the_noise_away_from_the_patches()
    {
        DirtPatches mask = Sandbox();
        int grass = 0, total = 0;
        for (float x = 0; x < 400; x += 4)
        for (float z = 0; z < 400; z += 4)
        {
            var p = new Vector2(x, z);
            if (SandboxMap.BushPatches.Any(c => Vector2.Distance(c, p) < 40f)) continue;
            total++;
            if (mask.At(x, z) < 0.5f) grass++;
        }
        Assert.InRange(grass / (float)total, 0.05f, 0.3f);
    }

    [Fact]
    public void The_island_edge_is_not_a_perfect_circle()
    {
        // Without the edge wobble a 20 m ring reads nearly the same value all round (span at most ~0.16), so > 0.2 needs the wobble.
        DirtPatches mask = new(400, 400, new[] { new Vector2(200, 200) }, 7);
        var values = Enumerable.Range(0, 36).Select(i =>
        {
            float a = i * MathF.PI / 18;
            return mask.At(200 + 20 * MathF.Cos(a), 200 + 20 * MathF.Sin(a));
        }).ToArray();
        Assert.True(values.Max() - values.Min() > 0.2f, "a ring at the island edge should cross from grass to dirt");
    }

    [Fact]
    public void Flat_ground_grass_covers_a_minority_of_the_map()
    {
        DirtPatches mask = Sandbox();
        int grass = 0, total = 0;
        for (float x = 0; x < 400; x += 3)
        for (float z = 0; z < 400; z += 3, total++)
            if (GroundCover.GrassWeight(x, z, 1f, mask.At(x, z)) >= 0.5f) grass++;
        Assert.InRange(grass / (float)total, 0.15f, 0.4f);
    }

    [Fact]
    public void Lookup_at_a_texel_centre_returns_that_texel()
    {
        var mask = new DirtPatches(256, 256, NoIslands, 7);   // 1 m per texel
        byte[] bytes = mask.ToBytes();
        int i = 100, j = 60;
        Assert.Equal(bytes[j * DirtPatches.Resolution + i] / 255f, mask.At(i + 0.5f, j + 0.5f), 4);
    }

    [Fact]
    public void Lookup_halfway_between_texel_centres_is_their_average()
    {
        var mask = new DirtPatches(256, 256, NoIslands, 7);
        byte[] bytes = mask.ToBytes();
        int i = 100, j = 60;
        float expected = (bytes[j * 256 + i] + bytes[j * 256 + i + 1]) / 2f / 255f;
        Assert.Equal(expected, mask.At(i + 1.0f, j + 0.5f), 4);
    }

    [Fact]
    public void Lookups_beyond_the_map_clamp_to_the_edge_texels()
    {
        var mask = new DirtPatches(256, 256, NoIslands, 7);
        Assert.Equal(mask.At(0f, 0f), mask.At(-50f, -50f), 5);
        Assert.Equal(mask.At(255.9f, 255.9f), mask.At(500f, 500f), 5);
    }
}
