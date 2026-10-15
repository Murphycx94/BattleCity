using Godot;
using Tank.Client.Art;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;

namespace Tank.Client.Combat;

/// <summary>
/// Cartoon tracers for live shells, pooled: a glowing head and a short tapering trail that grows to
/// <see cref="TrailLength"/> as the shell leaves the muzzle. AP is yellow, HE orange. Drawn one tick behind the sim to
/// match interpolated tanks.
/// </summary>
public partial class ShellViews : Node3D
{
    private const float TrailLength = 7f;

    private readonly List<Tracer> _pool = new();
    private readonly (Material Head, Material Trail) _ap = TracerMaterials(Palette.Flash.ToColor());
    private readonly (Material Head, Material Trail) _he = TracerMaterials(Palette.Fire.ToColor());

    private sealed class Tracer
    {
        public required Node3D Root;
        public required MeshInstance3D Head;
        public required MeshInstance3D Trail;
        public AmmoType Ammo;
    }

    private static (Material Head, Material Trail) TracerMaterials(Color color) => (
        new StandardMaterial3D
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 4f,
            DisableReceiveShadows = true,
        },
        new StandardMaterial3D
        {
            AlbedoColor = new Color(color, 0.7f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 2f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            DisableReceiveShadows = true,
        });

    /// <param name="alpha">Fraction of the way from the previous tick to the latest one.</param>
    /// <param name="frameDelta">Render frame time, seconds; the tracer covers at least the distance flown in one frame.</param>
    public void Sync(ReadOnlySpan<Shell> shells, float alpha, float frameDelta)
    {
        while (_pool.Count < shells.Length) _pool.Add(NewTracer());
        for (int i = 0; i < _pool.Count; i++)
        {
            Tracer tracer = _pool[i];
            Node3D root = tracer.Root;
            MeshInstance3D trail = tracer.Trail;
            root.Visible = i < shells.Length;
            if (!root.Visible) continue;
            Shell shell = shells[i];
            if (shell.Ammo != tracer.Ammo) Paint(tracer, shell.Ammo);
            float age = MathF.Max(shell.Age - (1f - alpha) * SimConstants.Dt, 0f);
            Vector3 position = (shell.Position - shell.Velocity * ((1f - alpha) * SimConstants.Dt)).ToGodot();
            float speed = shell.Velocity.Length();
            Vector3 heading = speed > 0.01f ? shell.Velocity.ToGodot() / speed : Vector3.Zero;
            if (heading == Vector3.Zero) root.Position = position;
            else if (Mathf.Abs(heading.Y) < 0.99f) root.LookAtFromPosition(position, position + heading, Vector3.Up);
            else root.Position = position;
            // At least TrailLength, and never shorter than the distance covered per render frame, so the tracer has no gaps.
            float length = MathF.Min(MathF.Max(TrailLength, speed * Mathf.Clamp(frameDelta, 1f / 240f, 1f / 30f)), age * speed);
            trail.Visible = length > 0.5f;
            trail.Scale = new Vector3(1, MathF.Max(length, 0.01f), 1);
            trail.Position = new Vector3(0, 0, length / 2);
        }
    }

    private void Paint(Tracer tracer, AmmoType ammo)
    {
        var (head, trail) = ammo == AmmoType.HE ? _he : _ap;
        tracer.Head.MaterialOverride = head;
        tracer.Trail.MaterialOverride = trail;
        tracer.Ammo = ammo;
    }

    private Tracer NewTracer()
    {
        var root = new Node3D();
        var head = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.14f, Height = 1.6f, RadialSegments = 8, Rings = 2 },
            MaterialOverride = _ap.Head,
            RotationDegrees = new Vector3(90, 0, 0),   // capsule axis Y → along the flight line
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(head);
        // Unit-height cone along +Z (behind the head), widest at the head; stretched to the trail length each frame.
        var trail = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.14f, Height = 1f, RadialSegments = 8, Rings = 1 },
            MaterialOverride = _ap.Trail,
            RotationDegrees = new Vector3(90, 0, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(trail);
        AddChild(root);
        return new Tracer { Root = root, Head = head, Trail = trail, Ammo = AmmoType.AP };
    }
}
