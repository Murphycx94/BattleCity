using Godot;

namespace Tank.Client.View;

/// <summary>Fire-and-forget particle effects. Every effect frees itself.</summary>
public partial class Fx : Node3D
{
    public float Shake { get; private set; }

    public override void _Process(double delta) => Shake = Mathf.Max(0, Shake - (float)delta * 3f);

    public void Explosion(Vector3 at, bool big)
    {
        float scale = big ? 1.6f : 1f;
        Burst(at, big ? 48 : 28, 0.7f * scale, 2.6f * scale, new[] { new Color(1f, 0.95f, 0.6f), new Color(1f, 0.45f, 0.1f), new Color(0.2f, 0.2f, 0.2f, 0f) }, 0.09f * scale, 0.8f);
        Burst(at, big ? 24 : 12, 0.4f * scale, 0.9f * scale, new[] { new Color(0.35f, 0.33f, 0.3f, 0.8f), new Color(0.15f, 0.15f, 0.15f, 0f) }, 0.18f * scale, 1.4f, gravity: 0.8f);
        Flash(at, new Color(1f, 0.6f, 0.25f), big ? 7f : 4f, big ? 4.5f : 3f);
        Shake = Mathf.Max(Shake, big ? 0.6f : 0.25f);
    }

    public void Spark(Vector3 at) =>
        Burst(at, 10, 0.15f, 1.6f, new[] { new Color(1f, 0.95f, 0.7f), new Color(1f, 0.6f, 0.2f, 0f) }, 0.035f, 0.35f);

    public void Muzzle(Vector3 at) => Flash(at, new Color(1f, 0.85f, 0.5f), 2.5f, 2f, 0.08f);

    public void Pickup(Vector3 at, Color color)
    {
        Burst(at, 30, 0.3f, 2.2f, new[] { color, new Color(color, 0f) }, 0.05f, 0.7f, gravity: -1f);
        Flash(at, color, 3f, 3f);
    }

    private void Burst(Vector3 at, int amount, float radius, float speed, Color[] ramp, float size, float life, float gravity = -4f)
    {
        var mesh = new SphereMesh { Radius = size, Height = size * 2, RadialSegments = 6, Rings = 3 };
        mesh.Material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var offsets = new float[ramp.Length];
        for (int i = 0; i < ramp.Length; i++) offsets[i] = ramp.Length == 1 ? 0 : i / (float)(ramp.Length - 1);
        var p = new CpuParticles3D
        {
            Position = at,
            Amount = amount,
            Lifetime = life,
            OneShot = true,
            Explosiveness = 0.95f,
            Mesh = mesh,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = radius,
            Direction = Vector3.Up,
            Spread = 180,
            InitialVelocityMin = speed * 0.4f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0, gravity, 0),
            DampingMin = 1.5f,
            DampingMax = 3f,
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.4f,
            ColorRamp = new Gradient { Offsets = offsets, Colors = ramp },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Emitting = true,
        };
        AddChild(p);
        GetTree().CreateTimer(life + 0.2).Timeout += p.QueueFree;
    }

    private void Flash(Vector3 at, Color color, float energy, float range, float duration = 0.35f)
    {
        var light = new OmniLight3D { Position = at + new Vector3(0, 0.6f, 0), LightColor = color, LightEnergy = energy, OmniRange = range };
        AddChild(light);
        var tween = light.CreateTween();
        tween.TweenProperty(light, "light_energy", 0.0, duration);
        tween.TweenCallback(Callable.From(light.QueueFree));
    }
}
