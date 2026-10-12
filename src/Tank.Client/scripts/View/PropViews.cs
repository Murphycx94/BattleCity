using Godot;
using Tank.Core.World;

namespace Tank.Client.View;

/// <summary>Small entities: bullets, power-ups and the eagle base.</summary>
public static class PropViews
{
    private static readonly Dictionary<PowerUpType, Color> PowerUpColors = new()
    {
        [PowerUpType.Helmet] = new Color(0.4f, 0.85f, 1f),
        [PowerUpType.Clock] = new Color(0.85f, 0.85f, 1f),
        [PowerUpType.Shovel] = new Color(0.75f, 0.75f, 0.8f),
        [PowerUpType.Star] = new Color(1f, 0.85f, 0.2f),
        [PowerUpType.Grenade] = new Color(1f, 0.35f, 0.25f),
        [PowerUpType.ExtraLife] = new Color(0.45f, 1f, 0.5f),
    };

    public static Node3D Bullet(bool power)
    {
        var root = new Node3D();
        root.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = power ? 0.11f : 0.085f, Height = power ? 0.22f : 0.17f, RadialSegments = 10, Rings = 6 },
            MaterialOverride = Palette.Glow(power ? new Color(1f, 0.55f, 0.25f) : Palette.Bullet, 4f),
            Position = new Vector3(0, 0.36f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return root;
    }

    public static Node3D PowerUp(PowerUpType type)
    {
        var root = new Node3D();
        var color = PowerUpColors.GetValueOrDefault(type, Colors.White);
        var gem = new MeshInstance3D
        {
            Name = "Gem",
            Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.5f, 0.5f) },
            MaterialOverride = Palette.Glow(color, 1.6f),
            Position = new Vector3(0.5f, 0.6f, 0.5f),
            RotationDegrees = new Vector3(45, 0, 35),
        };
        root.AddChild(gem);
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.42f, Height = 0.02f, RadialSegments = 24 },
            MaterialOverride = Palette.Glow(new Color(color, 0.35f), 1f),
            Position = new Vector3(0.5f, 0.02f, 0.5f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return root;
    }

    public static void AnimatePowerUp(Node3D node, double time, bool expiring)
    {
        var gem = node.GetNode<Node3D>("Gem");
        gem.Position = new Vector3(0.5f, 0.6f + 0.08f * Mathf.Sin((float)time * 3f), 0.5f);
        gem.Rotation = new Vector3(gem.Rotation.X, (float)time * 1.8f, gem.Rotation.Z);
        node.Visible = !expiring || (int)(time * 8) % 2 == 0;
    }

    public static Node3D Eagle(int team)
    {
        var root = new Node3D();
        var gold = Palette.Metal(Palette.Eagle);
        var accent = Palette.Matte(Palette.ForTeam(team));
        void Box(Vector3 size, Vector3 pos, Material m, string? name = null) =>
            root.AddChild(new MeshInstance3D { Name = name ?? string.Empty, Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = pos });
        Box(new Vector3(0.9f, 0.12f, 0.9f), new Vector3(0.5f, 0.06f, 0.5f), accent, "Plinth");
        Box(new Vector3(0.24f, 0.36f, 0.28f), new Vector3(0.5f, 0.32f, 0.52f), gold, "Body");
        Box(new Vector3(0.82f, 0.08f, 0.2f), new Vector3(0.5f, 0.42f, 0.56f), gold, "Wings");
        Box(new Vector3(0.14f, 0.14f, 0.16f), new Vector3(0.5f, 0.56f, 0.4f), gold, "Head");
        Box(new Vector3(0.06f, 0.05f, 0.1f), new Vector3(0.5f, 0.54f, 0.3f), Palette.Matte(new Color(1f, 0.5f, 0.1f)), "Beak");
        return root;
    }

    public static void SetEagleAlive(Node3D eagle, bool alive)
    {
        foreach (var name in new[] { "Body", "Wings", "Head", "Beak" })
        {
            var part = eagle.GetNode<MeshInstance3D>(name);
            part.MaterialOverride = alive ? part.MaterialOverride : Palette.Matte(Palette.EagleDead);
            part.Scale = alive ? Vector3.One : new Vector3(1, 0.35f, 1);
        }
    }
}
