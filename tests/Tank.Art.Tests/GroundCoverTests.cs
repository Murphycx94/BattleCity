using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class GroundCoverTests
{
    private static float NormalYForSlope(float degrees) => MathF.Cos(degrees * MathF.PI / 180f);

    [Fact]
    public void Value_noise_is_smooth_bounded_and_deterministic()
    {
        for (float x = -20f; x < 20f; x += 0.37f)
        for (float z = -20f; z < 20f; z += 0.41f)
        {
            float n = GroundCover.ValueNoise2(x, z);
            Assert.InRange(n, 0f, 1f);
            Assert.Equal(n, GroundCover.ValueNoise2(x, z));
            Assert.True(MathF.Abs(n - GroundCover.ValueNoise2(x + 0.01f, z)) < 0.1f);
        }
    }

    [Fact]
    public void Value_noise_matches_the_hash_at_whole_coordinates()
    {
        Assert.Equal(GroundCover.Hash12(3, 7), GroundCover.ValueNoise2(3f, 7f), 5);
        Assert.NotEqual(GroundCover.Hash12(3, 7), GroundCover.Hash12(7, 3));
    }

    [Fact]
    public void Flat_ground_without_dirt_is_all_grass() =>
        Assert.Equal(1f, GroundCover.GrassWeight(40f, 50f, 1f, 0f), 3);

    [Fact]
    public void Steep_ground_and_dirt_patches_have_no_grass()
    {
        Assert.Equal(0f, GroundCover.GrassWeight(40f, 50f, NormalYForSlope(45f), 0f), 3);
        Assert.Equal(0f, GroundCover.GrassWeight(40f, 50f, 1f, 1f), 3);
    }

    [Fact]
    public void Grass_fades_out_across_the_slope_band()
    {
        float Mean(float slope)
        {
            float sum = 0;
            int count = 0;
            for (float x = 0; x < 60; x += 1.7f)
            for (float z = 0; z < 60; z += 1.9f, count++) sum += GroundCover.GrassWeight(x, z, NormalYForSlope(slope), 0f);
            return sum / count;
        }
        Assert.True(Mean(15f) > 0.95f);
        Assert.True(Mean(29f) is > 0.1f and < 0.9f);
        Assert.True(Mean(36f) < 0.05f);
        Assert.True(Mean(15f) > Mean(24f) && Mean(24f) > Mean(29f) && Mean(29f) > Mean(36f));
    }

    [Fact]
    public void Dirt_edge_is_ragged_by_the_noise()
    {
        var weights = new HashSet<float>();
        for (float x = 0; x < 30; x += 0.5f) weights.Add(MathF.Round(GroundCover.GrassWeight(x, 10f, 1f, 0.5f), 2));
        Assert.True(weights.Count > 5, "a half-dirt mask should vary along the edge");
    }

    [Fact]
    public void Smoothstep_clamps()
    {
        Assert.Equal(0f, GroundCover.SmoothStep(1, 2, 0));
        Assert.Equal(1f, GroundCover.SmoothStep(1, 2, 3));
        Assert.Equal(0.5f, GroundCover.SmoothStep(1, 2, 1.5f), 5);
    }
}
