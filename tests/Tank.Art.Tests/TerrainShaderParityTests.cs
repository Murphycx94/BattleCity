using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

/// <summary>Pins the layer-weight constants in terrain_toon.gdshader to GroundCover's, so the two cannot drift apart.</summary>
public class TerrainShaderParityTests
{
    private static string Shader() => File.ReadAllText(Path.Combine(
        Path.GetDirectoryName(Here())!, "..", "..", "src", "Tank.Client", "shaders", "terrain_toon.gdshader"));

    private static string Here([CallerFilePath] string p = "") => p;

    private static float Private(string name) =>
        (float)typeof(GroundCover).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static string F(float v) => v.ToString("0.0###", CultureInfo.InvariantCulture);

    [Fact]
    public void Shader_layer_weight_rules_use_the_same_constants_as_GroundCover()
    {
        string s = Shader();
        float rock = GroundCover.RockMinSlopeDegrees, grass = GroundCover.GrassMaxSlopeDegrees;
        string[] expected =
        {
            $"value_noise2(world_pos.xz * {F(Private("SlopeNoiseScale"))}) - 0.5) * {F(Private("SlopeNoiseDegrees"))}",
            $"value_noise2(world_pos.xz * {F(Private("DirtNoiseScale"))}) - 0.5) * {F(Private("DirtNoiseAmount"))}",
            "smoothstep(0.25, 0.95, dirt_patch +",
            "smoothstep(rock_min_slope - 4.0, rock_min_slope + 3.0, slope)",
            "smoothstep(grass_max_slope - 2.0, grass_max_slope + 6.0, slope)",
            $"uniform float grass_max_slope = {F(grass)};",
            $"uniform float rock_min_slope = {F(rock)};",
        };
        foreach (string e in expected) Assert.Contains(e, s);
    }
}
