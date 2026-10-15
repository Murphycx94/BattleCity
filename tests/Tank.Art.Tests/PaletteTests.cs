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
