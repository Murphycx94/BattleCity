using Godot;
using Tank.Client.Art;

namespace Tank.Client.Combat;

/// <summary>
/// Little light-brown cartoon dust puffs popping up behind both tracks above 10 km/h, more of them the faster the tank
/// goes. Attach to the tank model; puffs are emitted in world space so they stay where they were kicked up.
/// </summary>
public partial class DustTrail : Node3D
{
    private const float StartSpeed = 10f / 3.6f;
    private const float FullSpeed = 45f / 3.6f;
    private const float RatioStep = 0.05f;

    private readonly List<GpuParticles3D> _emitters = new();
    private bool _emitting;
    private float _amountRatio = -1f;

    /// <param name="trackX">Track centre offset from the hull centre line.</param>
    /// <param name="rearZ">Hull-space Z just behind the tracks.</param>
    public void Build(float trackX, float rearZ)
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.25f, 0.05f, 0.2f),
            Direction = new Vector3(0, 1, 0.6f),
            Spread = 30f,
            InitialVelocityMin = 0.8f,
            InitialVelocityMax = 2f,
            DampingMin = 1.5f,
            DampingMax = 2.5f,
            Gravity = new Vector3(0, 0.2f, 0),
            ScaleMin = 0.45f,
            ScaleMax = 0.8f,
            ScaleCurve = ParticleKit.Pop(start: 0.2f, overshoot: 1.15f, peakAt: 0.2f, shrinkFrom: 0.45f),
            Color = Palette.SoilDust.ToColor(),
        };
        var drawPass = ParticleKit.PuffBall;
        foreach (float side in new[] { -1f, 1f })
        {
            var emitter = new GpuParticles3D
            {
                Amount = 24,
                Lifetime = 1.0,
                Emitting = false,
                LocalCoords = false,
                ProcessMaterial = process,
                DrawPass1 = drawPass,
                VisibilityAabb = ParticleKit.WideBounds,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(side * trackX, 0.15f, rearZ),
            };
            AddChild(emitter);
            _emitters.Add(emitter);
        }
    }

    public void Update(float speed)
    {
        float strength = Mathf.Clamp((MathF.Abs(speed) - StartSpeed) / (FullSpeed - StartSpeed), 0f, 1f);
        bool emitting = MathF.Abs(speed) > StartSpeed;
        float ratio = MathF.Round((0.35f + 0.65f * strength) / RatioStep) * RatioStep;
        // AmountRatio is just a value pushed to the RenderingServer; still, skip the calls (and the per-frame churn) unless
        // something changed. The ratio is quantised so a smoothly varying speed doesn't change it every frame.
        if (emitting != _emitting)
        {
            _emitting = emitting;
            foreach (GpuParticles3D emitter in _emitters) emitter.Emitting = emitting;
        }
        if (!Mathf.IsEqualApprox(ratio, _amountRatio))
        {
            _amountRatio = ratio;
            foreach (GpuParticles3D emitter in _emitters) emitter.AmountRatio = ratio;
        }
    }
}
