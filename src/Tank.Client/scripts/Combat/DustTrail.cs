using Godot;
using Tank.Client.Art;
using Tank.Client.World;

namespace Tank.Client.Combat;

/// <summary>
/// Little light-brown cartoon dust puffs popping up behind both tracks above 10 km/h, more of them the faster the tank
/// goes. Attach to the tank model; puffs are emitted in world space so they stay where they were kicked up.
/// In water (<see cref="SplashRules.Wading"/>, with its hysteresis: the depth under the hull centre, from the scene's
/// <see cref="WaterSurface"/>, looked up until there is one) the dust stops and white spray takes over: foam and drops
/// thrown out and up along both sides from the water line, harder the faster the tank goes
/// (<see cref="SplashRules.SprayStrength"/>). The spray emitters are only built on a map with water.
/// </summary>
public partial class DustTrail : Node3D
{
    private const float StartSpeed = 10f / 3.6f;
    private const float FullSpeed = 45f / 3.6f;
    private const float RatioStep = 0.05f;
    private const float SprayOut = 0.35f;        // spray rises this far outside the track centre line
    private const float WaterLineStep = 0.05f;   // move the spray emitters only when the water line moved this much

    private readonly Emitters _dust = new();
    private readonly Emitters _spray = new();
    private float _trackX;
    private float _rearZ;
    private WaterSurface? _water;   // looked up until found (the scene's world may come after the tank)
    private bool _wading;
    private float _waterLine;

    /// <param name="trackX">Track centre offset from the hull centre line.</param>
    /// <param name="rearZ">Hull-space Z just behind the tracks.</param>
    public void Build(float trackX, float rearZ)
    {
        _trackX = trackX;
        _rearZ = rearZ;
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
        foreach (float side in new[] { -1f, 1f })
            _dust.Add(this, Emitter(24, 1.0, process, new Vector3(side * trackX, 0.15f, rearZ)));
    }

    public void Update(float speed)
    {
        bool wading = _wading = SplashRules.Wading(WaterDepth(), _wading);
        float strength = Mathf.Clamp((MathF.Abs(speed) - StartSpeed) / (FullSpeed - StartSpeed), 0f, 1f);
        _dust.Set(!wading && MathF.Abs(speed) > StartSpeed, Quantise(0.35f + 0.65f * strength));
        if (!wading)
        {
            _spray.Set(false, _spray.Ratio);
            return;
        }
        float spray = SplashRules.SprayStrength(speed);
        _spray.Set(spray > 0f, Quantise(0.3f + 0.7f * spray));
    }

    /// <summary>The ratio is quantised so a smoothly varying speed doesn't change it every frame.</summary>
    private static float Quantise(float ratio) => MathF.Round(ratio / RatioStep) * RatioStep;

    /// <summary>
    /// Water depth under the hull centre; 0 without water. Until the scene has a water surface every call looks for it
    /// (cheap); the call that finds it builds the spray if the map has water. The spray emitters then follow the water
    /// line, which sits <c>depth</c> over the hull origin.
    /// </summary>
    private float WaterDepth()
    {
        if (_water is null)
        {
            _water = WaterSurface.Find(this);
            if (_water?.Level is not null) BuildSpray();
        }
        if (_water?.Level is null) return 0f;
        Vector3 at = GlobalPosition;
        float depth = _water.DepthAt(at.X, at.Z);
        if (depth > SplashRules.WadeOutDepth && MathF.Abs(depth - _waterLine) > WaterLineStep)
        {
            _waterLine = depth;
            _spray.SetHeight(depth);
        }
        return depth;
    }

    /// <summary>Per side: white foam puffs pushed out sideways, and small drops thrown up that fall back under gravity.</summary>
    private void BuildSpray()
    {
        var reach = new Vector3(0.2f, 0.05f, _rearZ * 0.75f);   // along most of the track
        var scale = ParticleKit.Pop(start: 0.3f, overshoot: 1.2f, peakAt: 0.15f, shrinkFrom: 0.5f);
        Color foam = Palette.Foam.ToColor();
        foreach (float side in new[] { -1f, 1f })
        {
            var at = new Vector3(side * (_trackX + SprayOut), 0f, 0f);
            _spray.Add(this, Emitter(32, 0.8, new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = reach,
                Direction = new Vector3(side, 0.9f, 0.2f),
                Spread = 25f,
                InitialVelocityMin = 1.5f,
                InitialVelocityMax = 3.2f,
                DampingMin = 2f,
                DampingMax = 3f,
                Gravity = new Vector3(0, -3f, 0),
                ScaleMin = 0.45f,
                ScaleMax = 0.85f,
                ScaleCurve = scale,
                Color = foam,
            }, at));
            _spray.Add(this, Emitter(24, 0.9, new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = reach,
                Direction = new Vector3(side * 0.7f, 1f, 0f),
                Spread = 30f,
                InitialVelocityMin = 2.5f,
                InitialVelocityMax = 5f,
                Gravity = new Vector3(0, -9.8f, 0),
                ScaleMin = 0.12f,
                ScaleMax = 0.24f,
                ScaleCurve = scale,
                Color = foam,
            }, at));
        }
    }

    private static GpuParticles3D Emitter(int amount, double lifetime, ParticleProcessMaterial process, Vector3 position) => new()
    {
        Amount = amount,
        Lifetime = lifetime,
        Emitting = false,
        LocalCoords = false,
        ProcessMaterial = process,
        DrawPass1 = ParticleKit.PuffBall,
        VisibilityAabb = ParticleKit.WideBounds,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Position = position,
    };

    /// <summary>A set of emitters switched and thinned together.</summary>
    private sealed class Emitters
    {
        private readonly List<GpuParticles3D> _all = new();
        private bool _emitting;

        public float Ratio { get; private set; } = -1f;

        public void Add(Node parent, GpuParticles3D emitter)
        {
            parent.AddChild(emitter);
            _all.Add(emitter);
        }

        /// <summary>
        /// AmountRatio is just a value pushed to the RenderingServer; still, skip the calls (and the per-frame churn)
        /// unless something changed.
        /// </summary>
        public void Set(bool emitting, float ratio)
        {
            if (emitting != _emitting)
            {
                _emitting = emitting;
                foreach (GpuParticles3D emitter in _all) emitter.Emitting = emitting;
            }
            if (!Mathf.IsEqualApprox(ratio, Ratio))
            {
                Ratio = ratio;
                foreach (GpuParticles3D emitter in _all) emitter.AmountRatio = ratio;
            }
        }

        /// <summary>Moves every emitter to <paramref name="y"/> over the hull origin.</summary>
        public void SetHeight(float y)
        {
            foreach (GpuParticles3D emitter in _all) emitter.Position = emitter.Position with { Y = y };
        }
    }
}
