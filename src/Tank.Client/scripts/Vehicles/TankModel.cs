using Godot;
using Tank.Client.World;
using Tank.Sim.Vehicles;

namespace Tank.Client.Vehicles;

/// <summary>
/// Low-poly tank built from boxes. Node layout mirrors the sim: hull origin on the ground, "Turret" at the ring
/// (yaw), "Gun" at the trunnion (pitch), barrel pointing -Z. A glTF model can replace it later if node names match.
/// </summary>
public partial class TankModel : Node3D
{
    private const float HullBottom = 0.4f;
    private const float HullTop = 1.3f;

    private static readonly Color HullColor = new(0.34f, 0.39f, 0.23f);
    private static readonly Color TurretColor = new(0.38f, 0.43f, 0.26f);
    private static readonly Color TrackColor = new(0.13f, 0.13f, 0.12f);
    private static readonly Color GunColor = new(0.27f, 0.30f, 0.19f);

    private readonly Node3D _turret = new() { Name = "Turret" };
    private readonly Node3D _gun = new() { Name = "Gun" };

    public void Build(TankSpec spec)
    {
        float w = spec.HullWidth, l = spec.HullLength;
        AddBox(this, new Vector3(w * 0.78f, HullTop - HullBottom, l * 0.96f), new Vector3(0, (HullTop + HullBottom) / 2, 0), HullColor);
        float trackX = w / 2 - w * 0.11f;
        foreach (float side in new[] { -1f, 1f })
            AddBox(this, new Vector3(w * 0.22f, 0.9f, l), new Vector3(side * trackX, 0.45f, 0), TrackColor);

        _turret.Position = new Vector3(0, 0, -spec.TurretOffset);
        AddChild(_turret);
        float turretTop = spec.TrunnionHeight + 0.35f;
        AddBox(_turret, new Vector3(w * 0.62f, turretTop - HullTop, l * 0.42f), new Vector3(0, (turretTop + HullTop) / 2, 0.4f), TurretColor);

        _gun.Position = new Vector3(0, spec.TrunnionHeight, 0);
        _turret.AddChild(_gun);
        AddBox(_gun, new Vector3(0.7f, 0.5f, 0.5f), new Vector3(0, 0, -0.1f), TurretColor); // mantlet
        _gun.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.13f, Height = spec.GunLength, RadialSegments = 8 },
            MaterialOverride = Flat(GunColor),
            Position = new Vector3(0, 0, -spec.GunLength / 2),
            RotationDegrees = new Vector3(-90, 0, 0),
        });
    }

    public void Apply(TankPose pose, float turretYaw, float gunPitch)
    {
        GlobalTransform = new Transform3D(new Basis(pose.Rotation.ToGodot()), pose.Position.ToGodot());
        _turret.Rotation = new Vector3(0, turretYaw, 0);
        _gun.Rotation = new Vector3(gunPitch, 0, 0);
    }

    private static void AddBox(Node3D parent, Vector3 size, Vector3 position, Color color) =>
        parent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Flat(color), Position = position });

    private static StandardMaterial3D Flat(Color color) => new() { AlbedoColor = color, Roughness = 0.85f };
}
