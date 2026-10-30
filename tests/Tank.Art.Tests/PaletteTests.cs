using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Tank.Client.Art;

namespace Tank.Art.Tests;

public class PaletteTests
{
    [Fact]
    public void Hex_reads_rrggbb_with_or_without_hash()
    {
        Assert.Equal(new Vector3(155, 217, 74) / 255f, Palette.Hex("#9BD94A"));
        Assert.Equal(Palette.Hex("#9BD94A"), Palette.Hex("9bd94a"));
        Assert.Throws<FormatException>(() => Palette.Hex("#12345"));
        Assert.Throws<FormatException>(() => Palette.Hex("#GG0000"));
    }

    [Fact]
    public void Design_table_colours_are_exact()
    {
        Assert.Equal(Palette.Hex("9BD94A"), Palette.GrassLight);
        Assert.Equal(Palette.Hex("4E9A33"), Palette.GrassDark);
        Assert.Equal(Palette.Hex("D4B48C"), Palette.Dirt);
        Assert.Equal(Palette.Hex("B0906A"), Palette.SlopeEarth);
        Assert.Equal(Palette.Hex("9E9890"), Palette.Rock);
        Assert.Equal(Palette.Hex("BDB6A8"), Palette.RockTop);
        Assert.Equal(Palette.Hex("4FA3E8"), Palette.SkyTop);
        Assert.Equal(Palette.Hex("DDF2FB"), Palette.SkyHorizon);
        Assert.Equal(Palette.Hex("5B5A8C"), Palette.Shadow);
        Assert.Equal(Palette.Hex("F28A2E"), Palette.TankBody);
        Assert.Equal(Palette.Hex("F5E6C8"), Palette.TankHub);
        Assert.Equal(Palette.Hex("3A3D4A"), Palette.Track);
        Assert.Equal(Palette.Hex("7CC243"), Palette.BushLight);
        Assert.Equal(Palette.Hex("3B7D2E"), Palette.BushDark);
        Assert.Equal(Palette.Hex("1E2333"), Palette.UiPanel);
        Assert.Equal(0.7f, Palette.UiPanelAlpha);
        Assert.Equal(Palette.Hex("FFC93C"), Palette.UiAccent);
        Assert.Equal(Palette.Hex("6F8FB5"), Palette.TargetBody);   // stage 2A design §4.2
        Assert.Equal(Palette.Hex("2B2622"), Palette.Charred);
    }

    [Fact]
    public void Wall_colours_keep_stone_apart_from_the_rocks_and_brick_apart_from_the_tanks()
    {
        Assert.True(Vector3.Distance(Palette.Stone, Palette.Rock) > 0.08f, "stone walls read as built, not as boulders");
        Assert.True(Vector3.Distance(Palette.Brick, Palette.TankBody) > 0.2f, "brick is redder than the orange tanks");
        Assert.True(Luminance(Palette.BrickMortar) - Luminance(Palette.Brick) > 0.15f, "cream joints show up on the bricks");
        Assert.True(Luminance(Palette.Stone) - Luminance(Palette.StoneMortar) > 0.15f, "dark joints show up on the stones");
    }

    [Fact]
    public void House_colours_start_from_the_riverland_values()
    {
        Assert.Equal(Palette.Hex("F1E4C8"), Palette.Plaster);
        Assert.Equal(Palette.Hex("B07A4A"), Palette.Planks);
        Assert.Equal(Palette.Hex("CF5E45"), Palette.RoofTile);
        Assert.Equal(Palette.Hex("5D6878"), Palette.RoofSlate);
        Assert.Equal(Palette.Hex("7A4B2E"), Palette.Door);
        Assert.Equal(Palette.Hex("A9DDF2"), Palette.WindowGlass);
        Assert.Equal(Palette.Hex("EADBC0"), Palette.PlasterDust);
        Assert.True(Vector3.Distance(Palette.Door, Palette.Planks) > 0.1f, "a door shows on a plank wall");
    }

    [Fact]
    public void Team_colours_keep_enemies_apart_from_everything_else_on_the_field()
    {
        Assert.Equal(Palette.TargetBody, Palette.AllyBody);
        Assert.True(Vector3.Distance(Palette.EnemyBody, Palette.Brick) >= 0.15f, "an enemy is not mistaken for a brick wall");
        Assert.True(Vector3.Distance(Palette.EnemyBody, Palette.TankBody) >= 0.3f, "an enemy is not mistaken for the player's orange tank");
        Assert.True(Vector3.Distance(Palette.EnemyBody, Palette.AllyBody) >= 0.25f, "an enemy is not mistaken for a teammate");
        Assert.True(Vector3.Distance(Palette.EnemyBody, Palette.HitPierced) >= 0.12f, "an enemy hull is not the red of a damage number");
    }

    [Fact]
    public void Ui_colours_are_the_designed_ones()
    {
        Assert.Equal(Palette.Hex("0B0E16"), Palette.UiBackground);
        Assert.Equal(Palette.Hex("8C94A3"), Palette.UiDim);
        Assert.Equal(Palette.Hex("73E68C"), Palette.UiGood);
        Assert.Equal(Palette.HitPierced, Palette.UiDanger);
        Assert.Equal(Palette.Hex("D8344A"), Palette.EnemyBody);
    }

    [Fact]
    public void The_zone_wall_is_a_light_blue_apart_from_the_sky_the_water_and_both_sides_tanks()
    {
        Assert.Equal(Palette.Hex("7CCBFF"), Palette.ZoneWall);
        Assert.True(Vector3.Distance(Palette.ZoneWall, Palette.SkyTop) >= 0.2f, "the wall shows against the sky");
        Assert.True(Vector3.Distance(Palette.ZoneWall, Palette.SkyHorizon) >= 0.3f, "the wall shows against the horizon");
        Assert.True(Vector3.Distance(Palette.ZoneWall, Palette.WaterShallow) >= 0.12f, "the wall is not the shallows' turquoise");
        Assert.True(Vector3.Distance(Palette.ZoneWall, Palette.WaterDeep) >= 0.3f, "the wall is not the deep water");
        Assert.True(Vector3.Distance(Palette.ZoneWall, Palette.AllyBody) >= 0.3f, "the wall is not a teammate's blue-grey");
        Assert.True(Vector3.Distance(Palette.ZoneWall, Palette.EnemyBody) >= 0.5f, "the wall is not an enemy's red");
        Assert.True(Vector3.Distance(Palette.ZoneWall, Palette.TankBody) >= 0.5f, "the wall is not the player's orange");
    }

    /// <summary>Relative luminance (Rec. 709 weights on the linear colour) of an sRGB palette entry.</summary>
    private static float Luminance(Vector3 srgb) => Vector3.Dot(Palette.Linear(srgb), new Vector3(0.2126f, 0.7152f, 0.0722f));

    [Fact]
    public void Linear_follows_the_srgb_curve()
    {
        Assert.Equal(0f, Palette.Linear(Vector3.Zero).X);
        Assert.Equal(1f, Palette.Linear(Vector3.One).Y, 5);
        Assert.Equal(0.2140f, Palette.Linear(new Vector3(0.5f)).Z, 3);
    }

    /// <summary>A shader uniform default tagged <c>// Palette.Name</c> must spell that entry's sRGB value to 3 decimals.</summary>
    [Fact]
    public void Shader_defaults_tagged_with_a_palette_name_match_it()
    {
        string shaders = Path.Combine(RepoRoot(), "src", "Tank.Client", "shaders");
        var tag = new Regex(@"vec3\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*\).*//\s*Palette\.(\w+)");
        int checkedCount = 0;
        foreach (string file in Directory.GetFiles(shaders, "*.gdshader*").Where(f => !f.EndsWith(".uid")))
        foreach (string line in File.ReadLines(file))
        {
            Match m = tag.Match(line);
            if (!m.Success) continue;
            FieldInfo field = typeof(Palette).GetField(m.Groups[4].Value, BindingFlags.Public | BindingFlags.Static)
                ?? throw new Xunit.Sdk.XunitException($"{Path.GetFileName(file)}: no Palette.{m.Groups[4].Value}");
            var expected = (Vector3)field.GetValue(null)!;
            var actual = new Vector3(Parse(m.Groups[1].Value), Parse(m.Groups[2].Value), Parse(m.Groups[3].Value));
            Assert.True(Vector3.Distance(expected, actual) < 0.002f, $"{Path.GetFileName(file)}: {line.Trim()} should be {expected}");
            checkedCount++;
        }
        Assert.True(checkedCount > 0, "no tagged shader defaults found");
    }

    private static float Parse(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private static string RepoRoot([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
