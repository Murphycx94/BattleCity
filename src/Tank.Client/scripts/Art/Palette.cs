using System.Globalization;
using System.Numerics;

namespace Tank.Client.Art;

/// <summary>
/// Every colour of the cartoon look, in one place (sRGB 0–1, before conversion to linear). Engine-neutral so the mesh
/// library and look data can use it too; <c>PaletteColors.ToColor</c> turns an entry into a Godot <c>Color</c>.
/// Shader uniforms that default to one of these carry a <c>// Palette.Name</c> comment, and a test keeps them in step.
/// </summary>
public static class Palette
{
    public static readonly Vector3 GrassLight = Hex("9BD94A");
    public static readonly Vector3 GrassDark = Hex("4E9A33");
    public static readonly Vector3 Dirt = Hex("D4B48C");         // light warm sand: the base colour of most of the ground
    public static readonly Vector3 SlopeEarth = Hex("B0906A");   // a deeper earth for steep slopes
    public static readonly Vector3 Rock = Hex("9E9890");
    public static readonly Vector3 RockTop = Hex("BDB6A8");
    public static readonly Vector3 SkyTop = Hex("4FA3E8");
    public static readonly Vector3 SkyHorizon = Hex("DDF2FB");
    public static readonly Vector3 Shadow = Hex("5B5A8C");
    public static readonly Vector3 TankBody = Hex("F28A2E");    // toy-orange hull, turret and gun
    public static readonly Vector3 TankHub = Hex("F5E6C8");     // cream wheel hubs and headlights
    public static readonly Vector3 Track = Hex("3A3D4A");       // tracks, tyres, muzzle bulb
    public static readonly Vector3 BushLight = Hex("7CC243");
    public static readonly Vector3 BushDark = Hex("3B7D2E");
    public static readonly Vector3 UiPanel = Hex("1E2333");
    public const float UiPanelAlpha = 0.7f;
    public static readonly Vector3 UiAccent = Hex("FFC93C");
    public static readonly Vector3 UiLampOff = Hex("4A4F5E");   // the spotted lamp while nobody sees the player
    public static readonly Vector3 TargetBody = Hex("6F8FB5");   // blue-grey practice targets (stage 2A design)
    public static readonly Vector3 Charred = Hex("2B2622");      // a knocked-out tank's scorched paint

    // Not in the design table; picked to sit with the colours above.
    public static readonly Vector3 Leaves = Hex("6CC04A");      // broadleaf crowns
    public static readonly Vector3 Needles = Hex("3E9A5B");     // conifer tiers, a cooler green
    public static readonly Vector3 Bark = Hex("9A6A43");        // warm brown trunks
    public static readonly Vector3 Flash = Hex("FFE45C");       // muzzle flash, tracer
    public static readonly Vector3 Fire = Hex("FF9A3C");        // fireball
    public static readonly Vector3 Smoke = Hex("F4F1EA");       // cartoon smoke puffs
    public static readonly Vector3 RockDust = Hex("C9CDD3");    // puffs from a shell on rock
    public static readonly Vector3 SoilDust = Hex("E3C79A");    // puffs from a shell on soil, track dust
    public static readonly Vector3 UiText = Hex("FFFFFF");
    public static readonly Vector3 UiOutline = Hex("141826");
    public static readonly Vector3 HitPierced = Hex("FF4D4D");   // "击穿！", damage numbers, a low health bar
    public static readonly Vector3 HitBlocked = Hex("B9BDC8");   // "未击穿" ("跳弹！" uses UiAccent)
    public static readonly Vector3 Health = Hex("6CD45A");       // a healthy health bar
    public static readonly Vector3 WreckSmoke = Hex("4A4650");   // dark smoke from a wreck
    public static readonly Vector3 Brick = Hex("C4553A");        // brick walls and their rubble
    public static readonly Vector3 BrickMortar = Hex("E8C9A8");  // cream joints between the bricks
    public static readonly Vector3 Stone = Hex("B4B8BF");        // stone walls: a cool grey, apart from the warm rocks
    public static readonly Vector3 StoneMortar = Hex("7E838C");  // darker joints between the stones
    public static readonly Vector3 Wood = Hex("B9844F");         // fences, and the chips off them
    public static readonly Vector3 BrickDust = Hex("E0A88C");    // dust off a brick wall

    /// <summary>"RRGGBB" (an optional leading '#') to sRGB 0–1.</summary>
    public static Vector3 Hex(string hex)
    {
        string h = hex.TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            throw new FormatException($"'{hex}' is not an RRGGBB colour.");
        return new Vector3((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF) / 255f;
    }

    /// <summary>sRGB 0–1 to linear, per channel (the standard piecewise curve), for vertex colours the shaders use as-is.</summary>
    public static Vector3 Linear(Vector3 srgb) => new(ToLinear(srgb.X), ToLinear(srgb.Y), ToLinear(srgb.Z));

    private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
}
