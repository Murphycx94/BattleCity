using Godot;
using Tank.Client.Art;

namespace Tank.Client.World;

/// <summary>
/// Cartoon sky (procedural gradient, sun glow and drifting clouds), a sun, and the post stack: Linear tone mapping so the
/// palette's colours reach the screen unchanged, no SSAO, a little glow for emissive effects only, light exponential
/// fog in the horizon colour, and FXAA instead of MSAA (MSAA was the biggest single frame cost on the M3).
/// </summary>
public static class Scenery
{
    /// <summary>The direction stage 1.5 lined up with the HDRI sun (elevation 48°, east of the spawn), kept so shadows don't move.</summary>
    public static readonly Vector3 SunDirection = new Vector3(0.5546f, 0.7420f, 0.3767f).Normalized();

    /// <summary>3D render scale. 1 = native; below 1 FSR 1 upscales (only if 60 fps needs it).</summary>
    public const float RenderScale = 1.0f;

    private static readonly Color SunColor = new(1.0f, 0.97f, 0.92f);
    private const float SunEnergy = 0.8f;
    private const float AmbientEnergy = 0.6f;
    private const float FogDensity = 0.0009f;

    public static WorldEnvironment CreateEnvironment()
    {
        var sky = new Sky
        {
            SkyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky_toon.gdshader") },
            RadianceSize = Sky.RadianceSizeEnum.Size64,
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            // A flat sky-coloured ambient: shadows and back faces pick up the sky's blue instead of going black.
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Palette.SkyTop.ToColor().Lerp(Palette.SkyHorizon.ToColor(), 0.5f),
            AmbientLightEnergy = AmbientEnergy,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,   // no specular anywhere in the toon look
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1.0f,
            SsaoEnabled = false,
            GlowEnabled = true,
            GlowIntensity = 0.45f,
            GlowBloom = 0f,
            GlowHdrThreshold = 1.1f,   // only emissive effects (flash, fireball, tracer) go over this
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Exponential,
            FogLightColor = Palette.SkyHorizon.ToColor(),
            FogDensity = FogDensity,
            FogSkyAffect = 0f,          // the sky shader already fades to the horizon colour
            FogSunScatter = 0f,
            FogAerialPerspective = 0f,
        };
        return new WorldEnvironment { Environment = environment };
    }

    public static DirectionalLight3D CreateSun() => new()
    {
        Basis = Basis.LookingAt(-SunDirection, Vector3.Up),
        LightColor = SunColor,
        LightEnergy = SunEnergy,
        LightAngularDistance = 0.8f, // soft shadow edges
        ShadowEnabled = true,
        ShadowBlur = 1.5f,
        DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
        DirectionalShadowMaxDistance = 300f,
        DirectionalShadowSplit1 = 0.05f,
        DirectionalShadowSplit2 = 0.15f,
        DirectionalShadowSplit3 = 0.4f,
    };

    /// <summary>
    /// FXAA instead of MSAA, optional FSR, soft shadow filter. Note this changes the root viewport (MSAA off, FXAA) and the
    /// global soft-shadow quality, which outlive a scene change; fine while only the Sandbox calls it.
    /// </summary>
    public static void ConfigureViewport(Viewport viewport)
    {
        viewport.Msaa3D = Viewport.Msaa.Disabled;
        viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
        viewport.Scaling3DMode = RenderScale < 1f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = RenderScale;
        RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftLow);
    }
}
