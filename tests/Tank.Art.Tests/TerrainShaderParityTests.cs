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

    /// <summary>The fragment function of the shader before the layered ground (5A batch D, as at 1ac8ca5), verbatim.</summary>
    private const string FragmentBeforeLayers = """
        void fragment() {
        	vec3 n = normalize(world_normal);
        	// Layer weights: keep in step with GroundCover.GrassWeight.
        	float slope = degrees(acos(clamp(n.y, 0.0, 1.0))) + (value_noise2(world_pos.xz * 0.35) - 0.5) * 6.0;
        	float dirt_patch = texture(dirt_mask, world_pos.xz / map_size).r;
        	dirt_patch = smoothstep(0.25, 0.95, dirt_patch + (value_noise2(world_pos.xz * 2.2) - 0.5) * 0.45);
        	float dry = smoothstep(dry_height, dry_height + 10.0, world_pos.y) * 0.6 * (0.35 + 0.65 * value_noise2(world_pos.xz * 0.15));
        	float rock_w = smoothstep(rock_min_slope - 4.0, rock_min_slope + 3.0, slope);
        	float slope_dirt = smoothstep(grass_max_slope - 2.0, grass_max_slope + 6.0, slope);
        	float dirt_w = max(max(slope_dirt, dirt_patch), dry) * (1.0 - rock_w);
        	// Colour only: mask dirt (the flat ground) is light sand, slope-driven dirt a deeper earth (weights above are unchanged).
        	float patchy = max(dirt_patch, dry);
        	vec3 dirt_tint = mix(dirt_color, slope_earth, slope_dirt / (slope_dirt + patchy + 0.0001));
        	float grass_w = max(1.0 - dirt_w - rock_w, 0.0);

        	vec3 grass = mix(grass_dark, grass_light, grass_tone);
        	vec3 albedo = grass * grass_w + dirt_tint * dirt_w + rock_color * rock_w;
        	ALBEDO = albedo * toon_mottle(world_pos.xz);
        	ROUGHNESS = 1.0;
        	METALLIC = 0.0;
        	SPECULAR = 0.0;
        }
        """;

    [Fact]
    public void Shader_layer_weight_rules_use_the_same_constants_as_GroundCover() => AssertWeightRules(Shader());

    /// <summary>
    /// The layered ground (riverland design §6.2) only recolours the dirt layer: its weight still comes from the mask's
    /// red channel through the same rules, so GroundCover keeps telling where grass grows, and sand and road blend by
    /// their share of that red channel (no earth verge on the ring R is dilated by). The wet band and the riverbed
    /// use ordered smoothstep edges and fade out over the outer 8 m, where the drawn ground sinks below the water level.
    /// </summary>
    [Fact]
    public void Layered_ground_keeps_the_grass_rule()
    {
        string s = Shader();
        AssertWeightRules(s);
        string[] expected =
        {
            "uniform bool layered",
            "uniform float water_level",
            "float not_grass = max(ground.r, 0.001);",
            "float sand_share = min(ground.g / not_grass, 1.0);",
            "float road_share = min(ground.b / not_grass, 1.0);",
            "smoothstep(0.15, 0.55, sand_share)",
            "smoothstep(0.2, 0.5, road_share)",
            "float dirt_patch = texture(dirt_mask, world_pos.xz / map_size).r;",
            "float inland = smoothstep(0.0, 8.0, min(min(world_pos.x, map_size.x - world_pos.x), min(world_pos.z, map_size.y - world_pos.z)));",
            "albedo = mix(albedo, wet_sand_color, (1.0 - smoothstep(water_level, water_level + 0.25, world_pos.y)) * inland);",
            "albedo = mix(albedo, riverbed_color, (1.0 - smoothstep(water_level - 0.4, water_level, world_pos.y)) * inland);",
        };
        foreach (string e in expected) Assert.Contains(e, s);
        Assert.DoesNotContain("smoothstep(water_level, water_level - 0.4", s);   // edge0 > edge1 is undefined in GLSL
    }

    /// <summary>
    /// Without layers (the sandbox) the ground is drawn exactly as before them: drop the <c>if (layered)</c> blocks and
    /// the fragment function is the old one, with <c>flat_dirt</c> standing for the <c>dirt_color</c> it is set to.
    /// </summary>
    [Fact]
    public void Without_layers_the_ground_is_drawn_as_before()
    {
        string s = Shader().Replace("\r\n", "\n");
        string fragment = s[s.IndexOf("void fragment() {", StringComparison.Ordinal)..].TrimEnd() + "\n";
        Assert.Contains("\tvec3 flat_dirt = dirt_color;\n", fragment);
        string plain = WithoutLayeredBlocks(fragment)
            .Replace("\tvec3 flat_dirt = dirt_color;\n", "")
            .Replace("mix(flat_dirt, slope_earth,", "mix(dirt_color, slope_earth,");
        Assert.Equal(FragmentBeforeLayers.Replace("\r\n", "\n") + "\n", plain);
    }

    private static void AssertWeightRules(string s)
    {
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

    /// <summary>The text with every <c>if (layered) { … }</c> block (its own lines, braces matched) taken out.</summary>
    private static string WithoutLayeredBlocks(string text)
    {
        const string Opening = "\tif (layered) {";
        while (text.IndexOf(Opening, StringComparison.Ordinal) is var at and >= 0)
        {
            int depth = 0, end = at + Opening.Length - 1;
            for (; end < text.Length; end++)
            {
                if (text[end] == '{') depth++;
                else if (text[end] == '}' && --depth == 0) break;
            }
            int lineEnd = text.IndexOf('\n', end) + 1;
            text = text[..at] + text[lineEnd..];
        }
        return text;
    }
}
