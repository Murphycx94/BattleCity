using Godot;
using Tank.Sim.Maps;

namespace Tank.Client.World;

/// <summary>One box per rock, matching the sim footprint and height exactly so what you see is what you hit.</summary>
public partial class PropMeshes : Node3D
{
    private static readonly Color RockColor = new(0.50f, 0.49f, 0.46f);

    public void Build(IReadOnlyList<Prop> props)
    {
        var material = new StandardMaterial3D { AlbedoColor = RockColor, Roughness = 0.95f };
        foreach (Prop prop in props)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(prop.Footprint.HalfExtents.X * 2, prop.Height, prop.Footprint.HalfExtents.Y * 2) },
                MaterialOverride = material,
                Position = prop.Footprint.Center.Ground(prop.Bottom + prop.Height / 2),
                Rotation = new Vector3(0, prop.Footprint.Yaw, 0),
            });
        }
    }
}
