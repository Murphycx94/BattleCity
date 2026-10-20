using Godot;
using Tank.Client.Vehicles;
using Tank.Sim;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>
/// The scenery of a battle (the sandbox's or a match's), built once from the battlefield: sky and sun, terrain, rocks,
/// bushes, walls and fences, grass and the tree line. Each frame <see cref="Update"/> lays the grass flat under the
/// followed tank (sized for whichever tank it is now), streams grass detail around the view, lets the bushes part
/// around the hull, and shows the props as they stand now (rubble, fallen fences, bushes shot away). With no tank to
/// follow (a free camera) nothing is flattened and the detail streams around the camera.
/// </summary>
public partial class BattleWorld : Node3D
{
    private const float GrassCameraLean = 0.4f;    // grass focus sits this fraction of the way from tank to camera
    private const float FootprintMargin = 0.15f;   // grass lies flat this far beyond the hull and tracks

    private Battlefield _field = null!;
    private GrassField _grass = null!;
    private BushField _bushes = null!;
    private WallViews _walls = null!;
    private SN.Vector2 _flatHalfExtents;
    private TankSpec? _flatFor;
    private bool _streamsAroundCamera;

    /// <param name="focus">The tank in view, for the first patch of grass streamed in.</param>
    /// <param name="style">The grass islands and dirt seed of this map.</param>
    /// <param name="streamsAroundCamera">The camera is a fixed far-away preset: stream detail around its eye, not the tank.</param>
    public void Build(Battlefield field, in TankState focus, MapStyle style, bool streamsAroundCamera)
    {
        _field = field;
        _streamsAroundCamera = streamsAroundCamera;
        AddChild(Scenery.CreateEnvironment());
        AddChild(Scenery.CreateSun());
        Scenery.ConfigureViewport(GetViewport());
        var terrain = new TerrainMesh();
        AddChild(terrain);
        var dirt = new DirtMask(field.Terrain.SizeX, field.Terrain.SizeZ, style.GrassIslands, style.Seed);
        terrain.Build(field.Terrain, dirt);
        var rocks = new RockMeshes();
        AddChild(rocks);
        rocks.Build(field.Props, field.Terrain);
        _bushes = new BushField();
        AddChild(_bushes);
        _bushes.Build(field.Props, field.Terrain);
        _walls = new WallViews();
        AddChild(_walls);
        _walls.Build(field);
        _grass = new GrassField();
        AddChild(_grass);
        _grass.Build(field.Terrain, dirt, field.Props);
        if (!streamsAroundCamera)
            _grass.Update(new Vector3(focus.Position.X, 0, focus.Position.Y), int.MaxValue);   // fill what is in range before the first frame
        var trees = new TreeLine();
        AddChild(trees);
        trees.Build(field);
    }

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
            _bushes.SyncStanding(_field);
            _bushes.Update(cameraPos, null, delta);
            _walls.Sync(_field, delta);
            return;
        }
        if (player.Spec != _flatFor) (_flatFor, _flatHalfExtents) = (player.Spec, FlatHalfExtents(player.Spec));
        _grass.SetTankFootprint(new Obb2(new SN.Vector2(ground.Position.X, ground.Position.Z), _flatHalfExtents, player.Yaw));
        // Detail streams around a point leaning from the tank toward the camera (so the ground right in front of a zoomed-out
        // camera is in the dense band); fixed far-away presets stream around their eye.
        Vector3 tankPos = ground.Position.ToGodot();
        _grass.Update(_streamsAroundCamera ? cameraPos
            : new Vector3(Mathf.Lerp(tankPos.X, cameraPos.X, GrassCameraLean), 0f, Mathf.Lerp(tankPos.Z, cameraPos.Z, GrassCameraLean)));
        _bushes.SyncStanding(_field);
        _bushes.Update(cameraPos, TankMotion.Footprint(player.Spec, player.Position, player.Yaw), delta);
        _walls.Sync(_field, delta);
    }

    /// <summary>Lets the walls see which way a fence was pushed down.</summary>
    public void OnEvent(in GameEvent e) => _walls.OnEvent(e);

    /// <summary>Half-size of the patch a tank lays flat: its hull and tracks, plus a margin.</summary>
    private static SN.Vector2 FlatHalfExtents(TankSpec spec)
    {
        TankLook look = TankLooks.For(spec.Id);
        float trackHalf = look.TrackX + look.TrackWidth / 2 + FootprintMargin;
        return new SN.Vector2(MathF.Max(spec.HullWidth / 2 + FootprintMargin, trackHalf), spec.HullLength / 2 + FootprintMargin);
    }
}
