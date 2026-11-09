using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>
/// Draws the map's houses (riverland design §6.5), each a cartoon house (<see cref="HouseShapes.House"/>) filling its
/// prop's box: the style nearest its size, one of <see cref="HouseShapes.Variants"/> looks per style picked by id
/// (<see cref="HouseShapes.SeedFor"/>), meshes shared. Each
/// is placed like a wall segment (turned by the footprint's yaw, standing on its plot's ground), with a hidden heap of
/// ruins (<see cref="HouseShapes.Ruin"/>) in the same place. <see cref="Sync"/> keeps them in step with the battlefield
/// and the camera:
/// - a house that is down sinks <see cref="CollapseSink"/> m and squashes to <see cref="CollapsedScale"/> of its height
///   over <see cref="CollapseSeconds"/>, then gives way to its ruins, heaped <see cref="HouseShapes.RuinShift"/> m the
///   way it was pushed (<see cref="OnEvent"/>; a push that comes after it started down, in a match, moves the heap
///   then, <see cref="PropDown"/>); a restored house stands whole again at once; the first look after
///   <see cref="Build"/> shows a house that is already down as its ruins straight away;
/// - houses within <see cref="SeeThroughFades.NearCamera"/> of the camera, and houses whose footprint (grown by
///   <see cref="Widen"/>) the ground line from the camera to the player's own tank crosses, fade
///   (<see cref="SeeThroughFades"/>; screen-door dithering in house.gdshader, as the trees) and back once clear. A
///   shadow-only copy of each house, never faded, keeps its shadow solid.
/// Per frame only the collapsing houses, the fading ones and the sectors around the camera and along that line are looked at.
/// </summary>
public partial class HouseViews : Node3D
{
    public const float SectorSize = ForestViews.SectorSize;
    private const float Widen = 0.5f;
    private const float CollapseSeconds = 0.5f;
    private const float CollapseSink = 1.5f;
    private const float CollapsedScale = 0.15f;
    private const float RuinSink = 0.1f;      // the heap sits this far into the ground, as the walls' rubble
    private const float RimStrength = 0.25f;  // the same rim light as the rocks and walls
    private static readonly StringName Opacity = "opacity";

    private readonly SectorGrid _grid = new(SectorSize);
    private readonly Dictionary<(int X, int Z), int[]> _sectors = new();   // sector → indices into _houses
    private readonly Dictionary<int, HouseView> _byId = new();
    private readonly List<HouseView> _collapsing = new();
    private readonly List<int> _faded = new();     // the houses whose opacity changed this frame
    private HouseView[] _houses = Array.Empty<HouseView>();
    private SeeThroughFades _fades = new(0);
    private float _reach;              // the widest house's half-diagonal, grown by Widen: how far one reaches out of its sector
    private Battlefield? _lastField;   // only to notice a resync's new instance; Sync is always given the current one
    private int _version = -1;

    private sealed class HouseView
    {
        public required int Id { get; init; }
        public required Node3D Body { get; init; }              // the house and its shadow-only copy
        public required MeshInstance3D Drawn { get; init; }     // the one seen (and dithered); its copy only casts the shadow
        public required MeshInstance3D Ruin { get; init; }
        public required Transform3D Upright { get; init; }
        public required Obb2 Bounds { get; init; }             // the footprint grown by Widen
        public required float Bottom { get; init; }
        public required float Top { get; init; }
        public PropDown Down = new(CollapseSeconds);
        public bool Whole = true;       // drawn whole or coming down, not yet its ruins: the only time it can be in the way
    }

    public void Build(Battlefield field)
    {
        ShaderMaterial material = Materials.FromShader("house").With("toon_rim_strength", RimStrength);
        var houses = new Dictionary<(float, float, float, uint), ArrayMesh>();
        var ruins = new Dictionary<(float, float, uint), ArrayMesh>();
        IReadOnlyList<Prop> props = field.Props;
        Dictionary<(int X, int Z), List<int>> sectors = SectorGrid.GroupByCentre(props, p => p.Kind == PropKind.House, SectorSize);
        var all = new List<HouseView>();
        foreach (var (key, indices) in sectors.OrderBy(entry => entry.Key))
        {
            var inSector = new int[indices.Count];
            for (int k = 0; k < indices.Count; k++)
            {
                Prop prop = props[indices[k]];
                var (length, width, height) = HouseShapes.SizeOf(prop);
                uint seed = HouseShapes.SeedFor(prop);
                if (!houses.TryGetValue((length, width, height, seed), out ArrayMesh? house))
                    houses[(length, width, height, seed)] = house = HouseShapes.House(HouseShapes.StyleFor(length, width, height), length, width, height, seed).ToArrayMesh();
                if (!ruins.TryGetValue((length, width, seed), out ArrayMesh? heap))
                    ruins[(length, width, seed)] = heap = HouseShapes.Ruin(length, width, seed).ToArrayMesh();
                HouseView view = AddHouse(prop, house, heap, material);
                _byId[prop.Id] = view;
                inSector[k] = all.Count;
                all.Add(view);
                _reach = MathF.Max(_reach, view.Bounds.HalfExtents.Length());
            }
            _sectors[key] = inSector;
        }
        _houses = all.ToArray();
        _fades = new SeeThroughFades(_houses.Length);
        _collapsing.Capacity = _houses.Length;
    }

    /// <summary>
    /// Notes which way a house was pushed, so its ruins heap that way when <see cref="Sync"/> sees it down; if it is down
    /// already (a match), moves its ruins that way now.
    /// </summary>
    public void OnEvent(in GameEvent e)
    {
        if (e.Type == GameEventType.PropDestroyed && _byId.TryGetValue(e.PropId, out HouseView? house)
            && house.Down.Pushed(new SN.Vector2(e.Velocity.X, e.Velocity.Z)))
            PlaceRuin(house);
    }

    /// <param name="field">The battlefield now (a resync swaps the instance): its houses' standing is read again whenever it
    /// or its <see cref="Battlefield.StateVersion"/> changes.</param>
    /// <param name="cameraPos">Camera position this frame.</param>
    /// <param name="ownHull">The player's own tank's hull; null for a free camera, where only the camera clears houses.</param>
    public void Sync(Battlefield field, Vector3 cameraPos, Obb2? ownHull, float delta)
    {
        if (_houses.Length == 0) return;
        if (!ReferenceEquals(field, _lastField) || field.StateVersion != _version)
        {
            bool first = _lastField is null;
            (_lastField, _version) = (field, field.StateVersion);
            SyncStanding(field, first);
        }
        Collapse(delta);
        SeeThroughHouses(cameraPos, ownHull, delta);
    }

    private HouseView AddHouse(in Prop prop, ArrayMesh house, ArrayMesh heap, Material material)
    {
        var upright = new Transform3D(new Basis(Vector3.Up, prop.Footprint.Yaw), prop.Footprint.Center.Ground(prop.Bottom + WallLine.Sink));
        var body = new Node3D { Transform = upright };
        AddChild(body);
        var drawn = new MeshInstance3D
        {
            Mesh = house,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,   // its dithering would speckle the shadow
        };
        body.AddChild(drawn);
        body.AddChild(new MeshInstance3D { Mesh = house, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly });
        var ruin = new MeshInstance3D { Mesh = heap, MaterialOverride = material, Visible = false };
        AddChild(ruin);
        return new HouseView
        {
            Id = prop.Id, Body = body, Drawn = drawn, Ruin = ruin, Upright = upright,
            Bounds = prop.Footprint with { HalfExtents = prop.Footprint.HalfExtents + new SN.Vector2(Widen) },
            Bottom = prop.Bottom + WallLine.Sink, Top = prop.Top,
        };
    }

    /// <summary>Puts a house's ruins in its place, <see cref="HouseShapes.RuinOffset"/> the way it was pushed.</summary>
    private static void PlaceRuin(HouseView house)
    {
        SN.Vector2 shift = HouseShapes.RuinOffset(house.Down.Push);
        house.Ruin.Transform = house.Upright.Translated(new Vector3(shift.X, -RuinSink, shift.Y));
    }

    /// <summary>Shows a house whole (<paramref name="whole"/>) or as its ruins.</summary>
    private static void ShowWhole(HouseView house, bool whole)
    {
        house.Whole = whole;
        house.Body.Visible = whole;
        house.Ruin.Visible = !whole;
    }

    /// <summary>
    /// Starts the houses knocked down since the last look coming down (on the <paramref name="first"/> look, shows them as
    /// their ruins at once), and stands restored ones up again at once.
    /// </summary>
    private void SyncStanding(Battlefield field, bool first)
    {
        foreach (HouseView house in _houses)
        {
            switch (house.Down.See(field.IsStanding(house.Id), snap: first))
            {
                case PropChange.Rose:
                    house.Body.Transform = house.Upright;
                    ShowWhole(house, true);
                    break;
                case PropChange.Fell:
                    PlaceRuin(house);
                    if (house.Down.Moving) _collapsing.Add(house);
                    else ShowWhole(house, false);
                    break;
            }
        }
    }

    /// <summary>
    /// Moves the collapsing houses on: sinking and squashing faster and faster, then swapped for their ruins. One stood up
    /// again meanwhile just drops off the list.
    /// </summary>
    private void Collapse(float delta)
    {
        for (int k = _collapsing.Count - 1; k >= 0; k--)
        {
            HouseView house = _collapsing[k];
            if (house.Down.Standing)
            {
                _collapsing.RemoveSwapAt(k);
                continue;
            }
            bool down = house.Down.Step(delta);
            float t = Math.Clamp(house.Down.Elapsed / CollapseSeconds, 0f, 1f);
            float progress = t * t;
            Basis squashed = house.Upright.Basis * Basis.FromScale(new Vector3(1f, Mathf.Lerp(1f, CollapsedScale, progress), 1f));
            house.Body.Transform = new Transform3D(squashed, house.Upright.Origin + Vector3.Down * (CollapseSink * progress));
            if (!down) continue;
            ShowWhole(house, false);
            _collapsing.RemoveSwapAt(k);
        }
    }

    /// <summary>
    /// Marks the houses in the way this frame (only those in the sectors near the camera and along the line to the tank,
    /// or all of them when those sectors are too many, and only while they are drawn whole or coming down), then fades every house in the way or still fading towards its
    /// opacity, uploading what changed.
    /// </summary>
    private void SeeThroughHouses(Vector3 camera, Obb2? ownHull, float delta)
    {
        _fades.BeginFrame();
        var eye = new SN.Vector2(camera.X, camera.Z);
        Span<(int X, int Z)> keys = stackalloc (int X, int Z)[SeeThroughFades.MaxSectors];
        int count = _grid.InView(eye, ownHull?.Center, SeeThroughFades.NearCamera, _reach, keys);
        if (count < 0)   // more sectors than keys: look at every house
        {
            for (int i = 0; i < _houses.Length; i++)
                if (_houses[i].Whole && InWay(_houses[i], camera, eye, ownHull)) _fades.MarkInWay(i);
        }
        for (int k = 0; k < count; k++)
        {
            if (!_sectors.TryGetValue(keys[k], out int[]? inSector)) continue;
            foreach (int i in inSector)
                if (_houses[i].Whole && InWay(_houses[i], camera, eye, ownHull)) _fades.MarkInWay(i);
        }
        _fades.Step(delta, _faded);
        foreach (int i in _faded) _houses[i].Drawn.SetInstanceShaderParameter(Opacity, _fades.OpacityOf(i));
    }

    /// <summary>
    /// Whether <paramref name="house"/>'s box (its footprint grown by <see cref="Widen"/>, ground to ridge) is within
    /// <see cref="SeeThroughFades.NearCamera"/> of the camera, or the ground line from the camera to
    /// <paramref name="ownHull"/>'s centre crosses that footprint.
    /// </summary>
    private static bool InWay(HouseView house, Vector3 camera, SN.Vector2 eye, Obb2? ownHull)
    {
        float horizontal = house.Bounds.DistanceToPoint(eye);
        float vertical = camera.Y > house.Top ? camera.Y - house.Top : camera.Y < house.Bottom ? house.Bottom - camera.Y : 0f;
        if (horizontal * horizontal + vertical * vertical < SeeThroughFades.NearCamera * SeeThroughFades.NearCamera) return true;
        return ownHull is { } own && SectorGrid.Crosses(house.Bounds, eye, own.Center);
    }
}
