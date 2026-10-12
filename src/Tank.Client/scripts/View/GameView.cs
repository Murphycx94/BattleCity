using Godot;
using Tank.Core;
using Tank.Core.Math;
using Tank.Core.World;
using Tank.Net.Client;
using Tank.Net.Protocol;

namespace Tank.Client.View;

/// <summary>
/// Maps network state onto the scene every frame: terrain, remote entities (interpolated), the local tank
/// and its bullets (predicted), and presentation events. Contains no gameplay logic.
/// </summary>
public partial class GameView : Node3D
{
    private readonly Dictionary<int, Node3D> _entities = new();
    private readonly HashSet<int> _seen = new();
    private readonly List<int> _stale = new();
    private readonly List<Node3D> _ownBullets = new();
    private TerrainView _terrain = null!;
    private CameraRig _rig = null!;
    private Fx _fx = null!;
    private TankView? _ownTank;
    private int _ownTankId;
    private GameMap? _boardFor;
    private bool _teamColors;
    private GameMap? _preview;
    private string? _previewName;

    public CameraRig Rig => _rig;

    public override void _Ready()
    {
        AddChild(new WorldEnvironment { Environment = BuildEnvironment() });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-58, -32, 0),
            LightColor = new Color(1f, 0.93f, 0.82f),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 60,
        });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-35, 150, 0),
            LightColor = new Color(0.55f, 0.65f, 1f),
            LightEnergy = 0.35f,
        });
        _terrain = new TerrainView();
        AddChild(_terrain);
        _fx = new Fx();
        AddChild(_fx);
        _rig = new CameraRig();
        AddChild(_rig);
    }

    /// <summary>
    /// Lobby backdrop: shows the selected map and fires one of every effect out of sight, so all shaders and
    /// pipelines are compiled before the match starts (no first-explosion hitch, docs/03 §9).
    /// </summary>
    public void ShowPreview(string mapName, float dt, double time)
    {
        if (_previewName != mapName)
        {
            _previewName = mapName;
            _preview = MapLibrary.Load(mapName).CreateMap();
            _boardFor = _preview;
            _rig.SetBoard((float)_preview.WidthSub / Units.Tile, (float)_preview.HeightSub / Units.Tile);
            ClearEntities();
            WarmUp(_preview);
        }
        _terrain.Sync(_preview!);
        _rig.UpdateRig(dt, null, 0, 0, time);
    }

    private void WarmUp(GameMap map)
    {
        var hidden = WorldMath.ToWorld(map.WidthSub / 2.0, map.HeightSub / 2.0, -1.5f); // under the ground slab
        _fx.Explosion(hidden, big: true);
        _fx.Explosion(hidden, big: false);
        _fx.Spark(hidden);
        _fx.Pickup(hidden, Palette.UiAccent);
        _fx.Muzzle(hidden);
        var samples = new List<Node3D> { PropViews.Bullet(false), PropViews.Bullet(true), PropViews.Eagle(0) };
        samples.AddRange(Enum.GetValues<PowerUpType>().Select(PropViews.PowerUp));
        foreach (var kind in Enum.GetValues<TankKind>())
        {
            var tank = new TankView();
            tank.Configure(Palette.Enemy, kind);
            samples.Add(tank);
        }
        foreach (var node in samples)
        {
            node.Position = hidden;
            AddChild(node);
            GetTree().CreateTimer(1.5).Timeout += node.QueueFree;
        }
    }

    public void Sync(GameClient client, float dt, double time)
    {
        var map = client.Map;
        if (map is null) return;
        if (!ReferenceEquals(map, _boardFor))
        {
            _boardFor = map;
            _previewName = null;
            _rig.SetBoard((float)map.WidthSub / Units.Tile, (float)map.HeightSub / Units.Tile);
            ClearEntities();
        }
        // In team mode colour tanks by side (who is with me?) instead of by player.
        _teamColors = client.MatchInfo?.Mode == GameMode.BaseBattle;
        _terrain.Sync(map);
        SyncRemote(client, dt, time);
        Vector3? playerPos = SyncOwnTank(client, dt, time);
        SyncOwnBullets(client);
        PlayEvents(client);
        _rig.UpdateRig(dt, playerPos, _ownTank?.Rotation.Y ?? 0, _fx.Shake, time);
    }

    private void SyncRemote(GameClient client, float dt, double time)
    {
        _seen.Clear();
        foreach (var re in client.Entities)
        {
            var e = re.Entity;
            _seen.Add(e.Id);
            if (!_entities.TryGetValue(e.Id, out var node))
            {
                node = Create(e);
                _entities[e.Id] = node;
                AddChild(node);
            }
            switch (e.Kind)
            {
                case EntityKind.Tank:
                    var kind = (TankKind)e.Type;
                    ((TankView)node).Apply(WorldMath.ToWorld(re.X + Units.TankSize / 2.0, re.Z + Units.TankSize / 2.0), (Dir)e.Dir, e.Flags, e.Hp, kind, time, dt);
                    break;
                case EntityKind.Bullet:
                    node.Position = WorldMath.ToWorld(re.X, re.Z);
                    break;
                case EntityKind.PowerUp:
                    node.Position = WorldMath.ToWorld(re.X, re.Z);
                    PropViews.AnimatePowerUp(node, time, e.Has(EntityFlags.Expiring));
                    break;
                case EntityKind.Eagle:
                    node.Position = WorldMath.ToWorld(re.X, re.Z);
                    PropViews.SetEagleAlive(node, e.Has(EntityFlags.Alive));
                    break;
            }
        }
        _stale.Clear();
        foreach (var id in _entities.Keys) if (!_seen.Contains(id)) _stale.Add(id);
        foreach (var id in _stale)
        {
            _entities[id].QueueFree();
            _entities.Remove(id);
        }
    }

    private Vector3? SyncOwnTank(GameClient client, float dt, double time)
    {
        var p = client.Predictor;
        if (!p.HasTank)
        {
            _ownTank?.QueueFree();
            _ownTank = null;
            return null;
        }
        if (_ownTank is null || _ownTankId != p.Current.Id)
        {
            _ownTank?.QueueFree();
            _ownTank = new TankView();
            _ownTank.Configure(_teamColors ? Palette.ForTeam(p.Current.Team) : Palette.ForSlot(client.Slot), TankKind.Player);
            _ownTankId = p.Current.Id;
            AddChild(_ownTank);
        }
        var (x, z) = client.OwnTankRenderPosition();
        var flags = EntityFlags.None;
        if (p.Current.Moving) flags |= EntityFlags.Moving;
        if (p.Current.SpawnTicks > 0) flags |= EntityFlags.Spawning;
        var snap = client.LatestSnapshot;
        if (snap is not null && snap.TryFind(p.Current.Id, out var authoritative))
            flags |= authoritative.Flags & (EntityFlags.Shield | EntityFlags.Frozen);
        var pos = WorldMath.ToWorld(x + Units.TankSize / 2.0, z + Units.TankSize / 2.0);
        _ownTank.Apply(pos, p.Current.Dir, flags, p.Current.Hp, TankKind.Player, time, dt);
        return pos;
    }

    private void SyncOwnBullets(GameClient client)
    {
        var bullets = client.Predictor.Bullets;
        int visible = 0;
        foreach (var b in bullets)
        {
            if (b.LocallyEnded) continue;
            if (visible == _ownBullets.Count)
            {
                var node = PropViews.Bullet(power: false);
                _ownBullets.Add(node);
                AddChild(node);
            }
            var view = _ownBullets[visible++];
            view.Visible = true;
            view.Position = WorldMath.ToWorld(b.State.X, b.State.Z);
        }
        for (int i = visible; i < _ownBullets.Count; i++) _ownBullets[i].Visible = false;
    }

    private void PlayEvents(GameClient client)
    {
        while (client.TryDequeueEvent(out var e))
        {
            var at = WorldMath.ToWorld(e.X, e.Z, 0.35f);
            switch (e.Type)
            {
                case GameEventType.BulletSpark: _fx.Spark(at); break;
                case GameEventType.TankHit:
                case GameEventType.ShieldBlocked: _fx.Spark(at); break;
                case GameEventType.TankDestroyed: _fx.Explosion(at, big: false); break;
                case GameEventType.EagleDestroyed: _fx.Explosion(at, big: true); break;
                case GameEventType.Fired: _fx.Muzzle(at); break;
                case GameEventType.PowerUpTaken: _fx.Pickup(at, Palette.UiAccent); break;
                case GameEventType.TankSpawned: _fx.Pickup(at, new Color(1, 1, 0.85f)); break;
            }
        }
    }

    private Node3D Create(NetEntity e)
    {
        switch (e.Kind)
        {
            case EntityKind.Tank:
                var tank = new TankView();
                var kind = (TankKind)e.Type;
                var color = kind != TankKind.Player ? Palette.Enemy
                    : _teamColors ? Palette.ForTeam(e.Team)
                    : Palette.ForSlot(e.Slot);
                tank.Configure(color, kind);
                return tank;
            case EntityKind.Bullet:
                return PropViews.Bullet(e.Has(EntityFlags.Power));
            case EntityKind.PowerUp:
                return PropViews.PowerUp((PowerUpType)e.Type);
            default:
                return PropViews.Eagle(e.Team);
        }
    }

    private void ClearEntities()
    {
        foreach (var node in _entities.Values) node.QueueFree();
        _entities.Clear();
        foreach (var b in _ownBullets) b.QueueFree();
        _ownBullets.Clear();
        _ownTank?.QueueFree();
        _ownTank = null;
    }

    private static Godot.Environment BuildEnvironment() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = Palette.Background,
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = new Color(0.55f, 0.6f, 0.75f),
        AmbientLightEnergy = 0.45f,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
        SsaoEnabled = true,
        SsaoRadius = 0.6f,
        SsaoIntensity = 1.6f,
        GlowEnabled = true,
        GlowIntensity = 0.7f,
        GlowBloom = 0.08f,
    };
}
