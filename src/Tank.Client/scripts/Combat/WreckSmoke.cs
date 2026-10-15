using Godot;
using Tank.Client.Art;

namespace Tank.Client.Combat;

/// <summary>
/// A steady column of dark cartoon puffs rising from a wreck: opaque toon balls that swell as they climb and shrink away
/// (like every other puff, no transparency). Attach to the tank model; puffs are emitted in world space.
/// </summary>
public partial class WreckSmoke : Node3D
{
    private GpuParticles3D _emitter = null!;

    /// <param name="at">Hull-space spot the smoke pours from (the open turret ring).</param>
    public void Build(Vector3 at)
    {
        _emitter = new GpuParticles3D
        {
            Amount = 14,
            Lifetime = 3.2,
            Emitting = false,
            LocalCoords = false,
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.6f,
                Direction = Vector3.Up,
                Spread = 12f,
                InitialVelocityMin = 1.2f,
                InitialVelocityMax = 2.0f,
                DampingMin = 0.2f,
                DampingMax = 0.4f,
                Gravity = new Vector3(0.25f, 0.5f, 0),   // a light breeze bends the column
                ScaleMin = 0.9f,
                ScaleMax = 1.5f,
                ScaleCurve = ParticleKit.Pop(start: 0.3f, overshoot: 1.6f, peakAt: 0.35f, shrinkFrom: 0.7f),
                Color = Palette.WreckSmoke.ToColor(),
            },
            DrawPass1 = ParticleKit.PuffBall,
            VisibilityAabb = ParticleKit.WideBounds,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = at,
        };
        AddChild(_emitter);
    }

    public bool Emitting
    {
        get => _emitter.Emitting;
        set => _emitter.Emitting = value;
    }
}
