using Godot;
using Tank.Core.Math;
using Tank.Core.World;
using Tank.Net.Protocol;

namespace Tank.Client.View;

/// <summary>Procedural low-poly tank. Silhouette varies by kind so enemy types read at a glance.</summary>
public partial class TankView : Node3D
{
    private const float TurnSpeed = 22f; // rad/s: turns finish within a couple of frames, like the original

    private readonly Node3D _body = new();
    private readonly StandardMaterial3D _paint = Palette.Matte(Colors.White, 0.6f);
    private readonly StandardMaterial3D _trackMat = Palette.Matte(new Color(0.12f, 0.12f, 0.13f), 0.95f);
    private MeshInstance3D _shield = null!;
    private MeshInstance3D _spawnStar = null!;
    private readonly List<MeshInstance3D> _treads = new();
    private Color _baseColor;
    private float _yaw;
    private bool _yawInitialized;
    private float _treadPhase;

    public void Configure(Color color, TankKind kind)
    {
        _baseColor = color;
        _paint.AlbedoColor = color;
        AddChild(_body);

        float hullW = kind == TankKind.Fast ? 0.66f : kind == TankKind.Armor ? 0.86f : 0.76f;
        float hullH = kind == TankKind.Armor ? 0.32f : 0.26f;
        float barrel = kind == TankKind.Power ? 0.62f : 0.48f;

        AddBox(new Vector3(hullW, hullH, 0.82f), new Vector3(0, 0.12f + hullH / 2, 0), _paint);
        AddBox(new Vector3(hullW * 0.9f, 0.05f, 0.2f), new Vector3(0, 0.12f + hullH + 0.02f, -0.3f), _paint); // glacis lip
        foreach (float side in new[] { -1f, 1f })
        {
            AddBox(new Vector3(0.17f, 0.24f, 0.94f), new Vector3(side * (hullW / 2 + 0.06f), 0.12f, 0), _trackMat);
            for (int i = 0; i < 5; i++)
            {
                var tread = AddBox(new Vector3(0.18f, 0.03f, 0.06f), new Vector3(side * (hullW / 2 + 0.06f), 0.25f, -0.4f + i * 0.2f), _trackMat);
                _treads.Add(tread);
            }
        }
        float turretY = 0.12f + hullH + 0.09f;
        AddBox(new Vector3(0.44f, 0.18f, 0.44f), new Vector3(0, turretY, 0.04f), _paint);
        var gun = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.065f, Height = barrel, RadialSegments = 10 },
            MaterialOverride = Palette.Metal(new Color(0.22f, 0.23f, 0.25f)),
            Position = new Vector3(0, turretY, -0.18f - barrel / 2),
            RotationDegrees = new Vector3(90, 0, 0),
        };
        _body.AddChild(gun);

        _shield = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.68f, Height = 1.0f, RadialSegments = 24, Rings = 12 },
            MaterialOverride = Palette.Glow(Palette.Shield, 1.2f),
            Position = new Vector3(0, 0.3f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_shield);

        _spawnStar = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.45f, 0.45f, 0.45f) },
            MaterialOverride = Palette.Glow(new Color(1, 1, 0.85f), 4f),
            Position = new Vector3(0, 0.45f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_spawnStar);
    }

    public void Apply(Vector3 position, Dir dir, EntityFlags flags, int hp, TankKind kind, double time, float dt)
    {
        Position = position;
        float target = WorldMath.Yaw(dir);
        if (!_yawInitialized)
        {
            _yaw = target;
            _yawInitialized = true;
        }
        _yaw = Mathf.RotateToward(_yaw, target, TurnSpeed * dt);
        _body.Rotation = new Vector3(0, _yaw, 0);

        bool spawning = (flags & EntityFlags.Spawning) != 0;
        _body.Visible = !spawning || ((int)(time * 12) % 2 == 0 && kind == TankKind.Player);
        _spawnStar.Visible = spawning;
        if (spawning)
        {
            float s = 0.6f + 0.4f * Mathf.Sin((float)time * 18f);
            _spawnStar.Scale = new Vector3(s, s, s);
            _spawnStar.Rotation = new Vector3(Mathf.Pi / 4, (float)time * 6f, Mathf.Pi / 4);
        }

        _shield.Visible = (flags & EntityFlags.Shield) != 0 && !spawning;
        if (_shield.Visible) _shield.Scale = Vector3.One * (1f + 0.04f * Mathf.Sin((float)time * 20f));

        _paint.AlbedoColor = Tint(flags, hp, kind, time);

        if ((flags & EntityFlags.Moving) != 0)
        {
            _treadPhase = (_treadPhase + dt * 3.5f) % 0.2f;
            for (int i = 0; i < _treads.Count; i++)
            {
                var p = _treads[i].Position;
                _treads[i].Position = new Vector3(p.X, p.Y, -0.4f + (i % 5) * 0.2f - _treadPhase);
            }
        }
    }

    private Color Tint(EntityFlags flags, int hp, TankKind kind, double time)
    {
        var c = _baseColor;
        if (kind == TankKind.Armor) c = c.Lerp(new Color(0.35f, 0.75f, 0.45f), Mathf.Clamp((hp - 1) / 3f, 0, 1));
        if ((flags & EntityFlags.Bonus) != 0 && (int)(time * 6) % 2 == 0) c = Palette.EnemyBonus;
        if ((flags & EntityFlags.Frozen) != 0) c = c.Lerp(new Color(0.6f, 0.85f, 1f), 0.6f);
        return c;
    }

    private MeshInstance3D AddBox(Vector3 size, Vector3 pos, Material mat)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat, Position = pos };
        _body.AddChild(mi);
        return mi;
    }
}
