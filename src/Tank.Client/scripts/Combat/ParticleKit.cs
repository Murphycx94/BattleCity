using Godot;
using Tank.Client.Art;

namespace Tank.Client.Combat;

/// <summary>
/// Shared building blocks for the cartoon effects: one low-poly ball mesh and one cube drawn with the toon puff shader
/// (opaque, coloured per particle), glowing materials for flashes, and "pop" scale curves (swell past full size, settle,
/// shrink to nothing).
/// </summary>
public static class ParticleKit
{
    /// <summary>Particles fly far from their emitter (LocalCoords off), so never cull them by the default small box.</summary>
    public static readonly Aabb WideBounds = new(new Vector3(-30, -10, -30), new Vector3(60, 40, 60));

    private static readonly Lazy<ShaderMaterial> PuffShader = new(() => Materials.FromShader("puff"));

    /// <summary>Unit-diameter ball for puffs and fireballs; coarse on purpose (cartoon, and cheap in numbers).</summary>
    public static SphereMesh Ball(Material material) => new() { Radius = 0.5f, Height = 1f, RadialSegments = 12, Rings = 6, Material = material };

    private static readonly Lazy<SphereMesh> SharedPuffBall = new(() => Ball(Puff));
    private static readonly Lazy<BoxMesh> SharedPuffBox = new(() => new BoxMesh { Size = Vector3.One, Material = Puff });
    private static readonly Dictionary<(Color, float), SphereMesh> GlowBalls = new();

    /// <summary>One shared puff ball mesh for every smoke, dust and debris emitter.</summary>
    public static SphereMesh PuffBall => SharedPuffBall.Value;

    /// <summary>One shared unit cube with the puff material, for every debris emitter's chunks (bricks, stones, wood, leaves).</summary>
    public static BoxMesh PuffBox => SharedPuffBox.Value;

    /// <summary>One shared ball mesh (with its glow material) per colour and energy.</summary>
    public static SphereMesh GlowBall(Color color, float energy)
    {
        if (!GlowBalls.TryGetValue((color, energy), out SphereMesh? mesh))
            GlowBalls[(color, energy)] = mesh = Ball(Glow(color, energy));
        return mesh;
    }

    /// <summary>Toon-lit opaque material that takes its colour from the particle (ParticleProcessMaterial.Color / ColorRamp).</summary>
    public static Material Puff => PuffShader.Value;

    /// <summary>Bright, glowing material for flashes and fireballs; energy above the glow threshold so bloom picks it up.</summary>
    public static StandardMaterial3D Glow(Color color, float energy) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
        DisableReceiveShadows = true,
        Roughness = 1f,
    };

    /// <summary>
    /// Scale over a particle's life: from <paramref name="start"/> it swells to <paramref name="overshoot"/> at
    /// <paramref name="peakAt"/>, springs back to 1, holds, then shrinks to nothing. Reads as a springy cartoon "pop".
    /// </summary>
    public static CurveTexture Pop(float start = 0.15f, float overshoot = 1.25f, float peakAt = 0.18f, float shrinkFrom = 0.55f)
    {
        var curve = new Curve { MaxValue = overshoot };
        curve.AddPoint(new Vector2(0f, start));
        curve.AddPoint(new Vector2(peakAt, overshoot));
        curve.AddPoint(new Vector2(peakAt * 1.8f, 0.92f));
        curve.AddPoint(new Vector2(peakAt * 2.6f, 1f));
        curve.AddPoint(new Vector2(shrinkFrom, 1f));
        curve.AddPoint(new Vector2(1f, 0f));
        return new CurveTexture { Curve = curve };
    }

    public static GpuParticles3D OneShot(int amount, double lifetime, float explosiveness, ParticleProcessMaterial process, Mesh drawPass) => new()
    {
        Amount = amount,
        Lifetime = lifetime,
        OneShot = true,
        Explosiveness = explosiveness,
        Emitting = false,
        LocalCoords = false,
        ProcessMaterial = process,
        DrawPass1 = drawPass,
        VisibilityAabb = WideBounds,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
}
