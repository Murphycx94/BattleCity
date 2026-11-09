using Godot;
using Tank.Client.Art.Meshes;
using Tank.Client.Vehicles;
using Tank.Sim;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>
/// The scenery of a battle (the sandbox's or a match's), built once from the battlefield: sky and sun, terrain, rocks,
/// bushes, walls and fences, water, the forests, the houses, grass and the tree line. Each frame <see cref="Update"/> lays
/// the grass flat under the followed tank (sized for whichever tank it is now), streams grass detail around the view, lets
/// the bushes part around the hull, shows the props as they stand now (rubble, fallen fences and trees, collapsing houses
/// and their ruins, bushes shot away) and makes the bushes, trees and houses in the way see-through. With no tank to follow (a free camera) nothing is flattened and the
/// detail streams around the camera.
/// </summary>
public partial class BattleWorld : Node3D
{
    private const float GrassCameraLean = 0.4f;    // grass focus sits this fraction of the way from tank to camera
    private const float FootprintMargin = 0.15f;   // grass lies flat this far beyond the hull and tracks

    private Battlefield _field = null!;
    private GrassField _grass = null!;
    private BushField _bushes = null!;
    private WallViews _walls = null!;
    private ForestViews _forest = null!;
    private HouseViews _houses = null!;
    private WorldEnvironment _environment = null!;
    private TreeLine _treeLine = null!;
    private SN.Vector2 _flatHalfExtents;
    private IReadOnlyList<TreeFit.ConiferRegion> _forests = Array.Empty<TreeFit.ConiferRegion>();
    private TankSpec? _flatFor;
    private bool _streamsAroundCamera;
    private Battlefield? _syncedField;   // the battlefield and state version the bushes and walls were last synced to
    private int _syncedVersion;

    /// <param name="focus">The tank in view, for the first patch of grass streamed in.</param>
    /// <param name="style">The grass islands and dirt seed of this map, used when the field has no ground kinds or water of
    /// its own, and which map it is: the forests' trees are conifers or broadleaves by its regions.</param>
    /// <param name="streamsAroundCamera">The camera is a fixed far-away preset: stream detail around its eye, not the tank.</param>
    public void Build(Battlefield field, in TankState focus, MapStyle style, bool streamsAroundCamera)
    {
        _field = field;
        (_syncedField, _syncedVersion) = (null, 0);   // the views below are new: the next frame syncs them, whatever was synced before
        _streamsAroundCamera = streamsAroundCamera;
        _environment = Scenery.CreateEnvironment();
        AddChild(_environment);
        AddChild(Scenery.CreateSun());
        Scenery.ConfigureViewport(GetViewport());
        var terrain = new TerrainMesh();
        AddChild(terrain);
        // A map with ground kinds or water brings its own ground; the sandbox's grass islands come from the style.
        DirtMask dirt = DirtMask.FromField(field) ?? new DirtMask(field.Terrain.SizeX, field.Terrain.SizeZ, style.GrassIslands, style.Seed);
        GroundMask = dirt;
        terrain.Build(field.Terrain, dirt, field.WaterLevel);
        var rocks = new RockMeshes();
        AddChild(rocks);
        rocks.Build(field.Props, field.Terrain);
        _bushes = new BushField();
        AddChild(_bushes);
        _bushes.Build(field.Props, field.Terrain);
        _walls = new WallViews();
        AddChild(_walls);
        _walls.Build(field);
        var water = new WaterSurface();
        AddChild(water);
        water.Build(field);
        _forests = TreeFit.ForestsOf(style.MapId);
        _forest = new ForestViews();
        AddChild(_forest);
        _forest.Build(field, _forests);
        _houses = new HouseViews();
        AddChild(_houses);
        _houses.Build(field);
        _grass = new GrassField();
        AddChild(_grass);
        _grass.Build(field.Terrain, dirt, field.Props);
        if (!streamsAroundCamera)
            _grass.Update(new Vector3(focus.Position.X, 0, focus.Position.Y), int.MaxValue);   // fill what is in range before the first frame
        _treeLine = new TreeLine();
        AddChild(_treeLine);
        _treeLine.Build(field);
    }

    /// <summary>
    /// For the top-down "map" preset, which looks at the whole map from far above: no fog, and the tree line and the
    /// forests drawn however far they are from the camera.
    /// </summary>
    public void UseMapView()
    {
        _environment.Environment.FogEnabled = false;
        _treeLine.SetFarView(true);
        _forest.SetFarView(true);
    }

    /// <summary>
    /// The bare-ground mask the terrain and the grass were built with (<see cref="Build"/>), for the minimap's ground; null
    /// before <see cref="Build"/>. Read-only: the scenery shares it.
    /// </summary>
    public DirtMask? GroundMask { get; private set; }

    /// <summary>The map's forests the trees were sorted into conifers and broadleaves by (<see cref="TreeFit.ForestsOf"/>), for the effects.</summary>
    public IReadOnlyList<TreeFit.ConiferRegion> Forests => _forests;

    /// <summary>A resync swapped the battlefield instance; the geometry is the same, only the props' current state is read from it.</summary>
    public void SetField(Battlefield field) => _field = field;

    /// <param name="tank">The tank the grass and bushes make room for, or null for a free camera.</param>
    /// <param name="pose">That tank on the ground this frame (null exactly when <paramref name="tank"/> is).</param>
    public void Update(TankState? tank, TankPose? pose, Vector3 cameraPos, float delta)
    {
        if (tank is not { } player || pose is not { } ground)
        {
            _grass.ClearTankFootprint();
            _grass.Update(cameraPos);
            SyncProps(delta);
            _bushes.Update(cameraPos, null, delta);
            SyncMapLayers(cameraPos, null, delta);
            return;
        }
        if (player.Spec != _flatFor) (_flatFor, _flatHalfExtents) = (player.Spec, FlatHalfExtents(player.Spec));
        _grass.SetTankFootprint(new Obb2(new SN.Vector2(ground.Position.X, ground.Position.Z), _flatHalfExtents, player.Yaw));
        // Detail streams around a point leaning from the tank toward the camera (so the ground right in front of a zoomed-out
        // camera is in the dense band); fixed far-away presets stream around their eye.
        Vector3 tankPos = ground.Position.ToGodot();
        _grass.Update(_streamsAroundCamera ? cameraPos
            : new Vector3(Mathf.Lerp(tankPos.X, cameraPos.X, GrassCameraLean), 0f, Mathf.Lerp(tankPos.Z, cameraPos.Z, GrassCameraLean)));
        SyncProps(delta);
        Obb2 hull = TankMotion.Footprint(player.Spec, player.Position, player.Yaw);
        _bushes.Update(cameraPos, hull, delta);
        SyncMapLayers(cameraPos, hull, delta);
    }

    /// <summary>Lets the walls, the forests and the houses see which way a fence, a tree or a house was pushed down.</summary>
    public void OnEvent(in GameEvent e)
    {
        _walls.OnEvent(e);
        _forest.OnEvent(e);
        _houses.OnEvent(e);
    }

    /// <summary>
    /// Shows the bushes, walls and fences as the battlefield has them: every one is looked at only when the battlefield
    /// instance (a resync) or its <see cref="Battlefield.StateVersion"/> changed since the last look; the fences on their way
    /// down move on every frame.
    /// </summary>
    private void SyncProps(float delta)
    {
        if (!ReferenceEquals(_field, _syncedField) || _field.StateVersion != _syncedVersion)
        {
            (_syncedField, _syncedVersion) = (_field, _field.StateVersion);
            _bushes.SyncStanding(_field);
            _walls.SyncStanding(_field);
        }
        _walls.Fall(delta);
    }

    /// <summary>
    /// The map's props drawn beyond the sandbox's kinds: the forests' trees fall and the houses come down, and both clear
    /// the view to <paramref name="hull"/> (null: a free camera).
    /// </summary>
    private void SyncMapLayers(Vector3 cameraPos, Obb2? hull, float delta)
    {
        _forest.Sync(_field, cameraPos, hull, delta);
        _houses.Sync(_field, cameraPos, hull, delta);
    }

    /// <summary>Half-size of the patch a tank lays flat: its hull and tracks, plus a margin.</summary>
    private static SN.Vector2 FlatHalfExtents(TankSpec spec)
    {
        TankLook look = TankLooks.For(spec.Id);
        float trackHalf = look.TrackX + look.TrackWidth / 2 + FootprintMargin;
        return new SN.Vector2(MathF.Max(spec.HullWidth / 2 + FootprintMargin, trackHalf), spec.HullLength / 2 + FootprintMargin);
    }
}
