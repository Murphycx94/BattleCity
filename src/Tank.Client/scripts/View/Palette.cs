using Godot;

namespace Tank.Client.View;

/// <summary>
/// Visual direction: a warm "toy diorama" — chunky low-poly blocks on a dark slate table, amber key light,
/// saturated team colours that read instantly from a high camera. All colours live here.
/// </summary>
public static class Palette
{
    public static readonly Color Background = new(0.045f, 0.055f, 0.085f);
    public static readonly Color Ground = new(0.16f, 0.17f, 0.19f);
    public static readonly Color GroundLine = new(0.22f, 0.23f, 0.26f);
    public static readonly Color Frame = new(0.32f, 0.33f, 0.36f);
    public static readonly Color BrickA = new(0.70f, 0.30f, 0.17f);
    public static readonly Color BrickB = new(0.60f, 0.24f, 0.13f);
    public static readonly Color Steel = new(0.72f, 0.75f, 0.80f);
    public static readonly Color Water = new(0.10f, 0.42f, 0.78f, 0.82f);
    public static readonly Color Forest = new(0.16f, 0.52f, 0.20f, 0.88f);
    public static readonly Color Ice = new(0.78f, 0.90f, 0.97f);
    public static readonly Color Eagle = new(0.95f, 0.78f, 0.25f);
    public static readonly Color EagleDead = new(0.25f, 0.22f, 0.2f);
    public static readonly Color Enemy = new(0.62f, 0.64f, 0.66f);
    public static readonly Color EnemyBonus = new(0.95f, 0.15f, 0.2f);
    public static readonly Color Bullet = new(1.0f, 0.93f, 0.6f);
    public static readonly Color Shield = new(0.45f, 0.85f, 1.0f, 0.28f);

    public static readonly Color UiAccent = new(1.0f, 0.72f, 0.18f);
    public static readonly Color UiText = new(0.92f, 0.93f, 0.95f);
    public static readonly Color UiDim = new(0.55f, 0.58f, 0.64f);
    public static readonly Color UiPanel = new(0.07f, 0.08f, 0.12f, 0.92f);
    public static readonly Color UiDanger = new(1.0f, 0.35f, 0.3f);
    public static readonly Color UiGood = new(0.45f, 0.9f, 0.55f);

    private static readonly Color[] SlotColors =
    {
        new(1.00f, 0.74f, 0.15f), // amber
        new(0.20f, 0.85f, 0.80f), // teal
        new(0.95f, 0.35f, 0.75f), // magenta
        new(0.55f, 0.90f, 0.25f), // lime
        new(0.35f, 0.60f, 1.00f), // sky
        new(1.00f, 0.50f, 0.20f), // orange
        new(0.70f, 0.50f, 1.00f), // violet
        new(0.95f, 0.95f, 0.95f), // white
    };

    private static readonly Color[] TeamColors = { new(1.00f, 0.74f, 0.15f), new(0.30f, 0.65f, 1.00f) };

    public static Color ForSlot(int slot) => SlotColors[((slot % SlotColors.Length) + SlotColors.Length) % SlotColors.Length];

    public static Color ForTeam(int team) => team is 0 or 1 ? TeamColors[team] : ForSlot(team);

    public static StandardMaterial3D Matte(Color c, float roughness = 0.85f) => new()
    {
        AlbedoColor = c,
        Roughness = roughness,
        Transparency = c.A < 0.999f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
    };

    public static StandardMaterial3D Metal(Color c) => new() { AlbedoColor = c, Metallic = 0.75f, Roughness = 0.35f };

    public static StandardMaterial3D Glow(Color c, float energy = 2.5f) => new()
    {
        AlbedoColor = c,
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = c.A < 0.999f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
    };

    public static StandardMaterial3D VertexColored(float roughness = 0.8f) => new()
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Roughness = roughness,
    };
}
