using Tank.Client.UI;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Terrain;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// A fixed camera's eye and focus on one map: ground positions (X is world X, Y is world Z), each with a height above
/// the ground there, or above the water where that is higher.
/// </summary>
public sealed record CameraAnchor(SN.Vector2 Eye, float EyeHeight, SN.Vector2 Focus, float FocusHeight);

/// <summary>
/// What the test drive needs to know about one map (riverland design §6.1): the map itself, the window title, where the
/// player starts and faces, the practice targets, whether the patrolling target drives round, the fixed cameras that
/// look at named places (<see cref="Anchors"/>) and where some presets park the player instead (<see cref="PresetSpawns"/>;
/// any other preset uses <see cref="Spawn"/>), the spin camera's eye, the middle and size of the map (the top-down
/// <c>map</c> view), and the camera presets and range demos that work on it.
/// </summary>
public sealed record MapSite(MapDefinition Map, string Title, SN.Vector2 Spawn, float SpawnYaw, IReadOnlyList<TargetSpot> Targets,
    bool HasPatrol, IReadOnlyDictionary<string, CameraAnchor> Anchors, IReadOnlyDictionary<string, SN.Vector2> PresetSpawns,
    SN.Vector2 SpinEye, SN.Vector2 MapCentre, float MapSize,
    IReadOnlyList<string> PresetNames, IReadOnlyList<string> DemoNames, Func<string?, RangeDemo?> FindDemo);

/// <summary>The test drive's sites, one per map in <see cref="MapCatalog"/>.</summary>
public static class MapSites
{
    /// <summary>The presets that follow the tank in its own frame, the same on every map.</summary>
    private static readonly string[] TankPresets = { "front", "side", "close", "closerear", "sprocket" };

    /// <summary>The base cameras' eye: this far from the base's centre towards its team's spawns, and this high over the ground there.</summary>
    private const float BaseEyeBack = 45f, BaseEyeHeight = 30f;

    /// <summary>The near base cameras' eye: at about a tank's sight height, this far from the centre (5 m outside a 25 m base's edge).</summary>
    private const float NearEyeBack = 30f, NearEyeHeight = 4f;

    /// <summary>
    /// The far base cameras frame what the follow camera sees from a tank at the spawn distance (<see cref="FarTankBack"/>)
    /// facing its base: <c>OrbitCamera</c>'s 14 m orbit at the match's 0.25 rad start pitch puts the eye
    /// <see cref="FollowBack"/> m behind the hull and <see cref="FollowEyeHeight"/> m up, looking at the point
    /// <see cref="FollowFocusHeight"/> m over it.
    /// </summary>
    private const float FarTankBack = 46f, FollowFocusHeight = 4.5f;
    private static readonly float FollowBack = 14f * MathF.Cos(0.25f), FollowEyeHeight = FollowFocusHeight + 14f * MathF.Sin(0.25f);

    /// <summary>
    /// The anchors of a map with bases, for match screenshots of the base rings, each looking at a base from the side its
    /// own team comes from (the line from the base's centre to the middle of its team's spawns): <c>base0</c> and
    /// <c>base1</c> from above (focus on the ground at the centre, eye <see cref="BaseEyeBack"/> m out,
    /// <see cref="BaseEyeHeight"/> m up); <c>base0near</c> and <c>base1near</c> from a tank's height (focus at the centre,
    /// eye <see cref="NearEyeBack"/> m out, <see cref="NearEyeHeight"/> m up): whether the band shows over the grass;
    /// <c>base0far</c> and <c>base1far</c> as the follow camera behind a tank <see cref="FarTankBack"/> m out facing the
    /// base: whether the outer line holds together that far off. A map without bases has none.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, CameraAnchor>> BaseAnchors(MapDefinition map)
    {
        foreach (TeamBase b in map.Bases)
        {
            SN.Vector2 home = SN.Vector2.Zero;
            int count = 0;
            foreach (SpawnPoint p in map.Spawns.Team)
                if (p.Team == b.Team) (home, count) = (home + p.Position, count + 1);
            SN.Vector2 back = SN.Vector2.Normalize(home / count - b.Centre);
            string name = $"base{b.Team}";
            yield return new(name, new CameraAnchor(b.Centre + back * BaseEyeBack, BaseEyeHeight, b.Centre, 0f));
            yield return new(name + "near", new CameraAnchor(b.Centre + back * NearEyeBack, NearEyeHeight, b.Centre, 0f));
            yield return new(name + "far", new CameraAnchor(b.Centre + back * (FarTankBack + FollowBack), FollowEyeHeight,
                b.Centre + back * FarTankBack, FollowFocusHeight));
        }
    }

    /// <summary>The base cameras' names (<see cref="BaseAnchors"/>), for <see cref="MapSite.PresetNames"/>.</summary>
    private static readonly string[] BasePresets = { "base0", "base1", "base0near", "base1near", "base0far", "base1far" };

    /// <summary>
    /// The sandbox as the test drive always was: its spawn, targets and patrol, the bush and patrol presets parking the
    /// player in the bush patch nearest the spawn and at the patrol's watch point. Its cameras stay the constants in
    /// <c>CameraPresets</c>; its only anchors are the base cameras (<see cref="BaseAnchors"/>).
    /// </summary>
    public static MapSite Sandbox { get; } = new(
        MapCatalog.Sandbox, MapText.SandboxTitle(SandboxMap.Id), SandboxMap.Spawn, SandboxMap.SpawnYaw, SandboxMap.TargetSpots,
        HasPatrol: true,
        Anchors: new Dictionary<string, CameraAnchor>(BaseAnchors(MapCatalog.Sandbox)),
        PresetSpawns: new Dictionary<string, SN.Vector2> { ["bush"] = SandboxMap.BushPatches[0], ["patrol"] = SandboxMap.PatrolWatch },
        SpinEye: new SN.Vector2(200, 200), MapCentre: new SN.Vector2(200, 200), MapSize: 400f,
        PresetNames: TankPresets.Concat(new[] { "hill", "spin", "orbit", "bush", "target", "yard", "gate", "patrol" }).Concat(BasePresets).ToArray(),
        DemoNames: RangeDemo.Names, FindDemo: RangeDemo.Find);

    /// <summary>
    /// The ford preset's start: on the east-west road in the town, 15 m short of the water's edge on the river's west
    /// bank (read off the generated map; <c>MapSitesTests</c> checks it). It faces east into the ford, like the spawn.
    /// </summary>
    private static readonly SN.Vector2 FordStart = new(665, 553);

    private const float RiverlandSize = (RiverlandMap.Samples - 1) * RiverlandMap.CellSize;

    /// <summary>
    /// The riverland (riverland design §3.11, §6.8): the spawn on the east-west road west of the town, the eight targets,
    /// no patrol; cameras over the town, its ford, the east forest's edge, the plateau, the dunes, the whole map from
    /// straight above and the two bases; the spin camera turns in the middle of the map; its own range demos (<see cref="RiverlandDemos"/>).
    /// </summary>
    public static MapSite Riverland { get; } = new(
        MapCatalog.Riverland, MapText.SandboxTitle(RiverlandMap.Id), RiverlandMap.Spawn, RiverlandMap.SpawnYaw, RiverlandMap.TargetSpots,
        HasPatrol: false,
        Anchors: new Dictionary<string, CameraAnchor>
        {
            ["overview"] = new(new SN.Vector2(560, 720), 120f, new SN.Vector2(790, 560), 0f),
            ["town"] = new(new SN.Vector2(640, 610), 25f, new SN.Vector2(705, 560), 3f),
            ["ford"] = new(new SN.Vector2(672, 592), 9f, new SN.Vector2(700, 560), 0f),
            ["forest"] = new(new SN.Vector2(850, 640), 8f, new SN.Vector2(905, 600), 4f),
            ["plateau"] = new(new SN.Vector2(330, 400), 40f, new SN.Vector2(240, 280), 2f),   // the focus on the plateau's top, ~22 m over the plain
            ["dunes"] = new(new SN.Vector2(900, 1000), 25f, new SN.Vector2(1020, 1060), 2f),
            ["map"] = new(RiverlandMap.Landmarks.MapCentre, 700f, RiverlandMap.Landmarks.MapCentre, 0f),
        }.Concat(BaseAnchors(MapCatalog.Riverland)).ToDictionary(),
        PresetSpawns: new Dictionary<string, SN.Vector2> { ["ford"] = FordStart },
        SpinEye: RiverlandMap.Landmarks.MapCentre, MapCentre: RiverlandMap.Landmarks.MapCentre, MapSize: RiverlandSize,
        PresetNames: TankPresets.Concat(new[] { "spin", "orbit", "target", "overview", "town", "ford", "forest", "plateau", "dunes", "map" }).Concat(BasePresets).ToArray(),
        DemoNames: RiverlandDemos.Names, FindDemo: RiverlandDemos.Find);

    /// <summary>Every site, the sandbox first.</summary>
    public static IReadOnlyList<MapSite> All { get; } = new[] { Sandbox, Riverland };

    /// <summary>
    /// The height fixed cameras (the anchors' heights, <c>CameraPresets.Get</c>) stand over at (x, z): the ground, or the
    /// water over it where the field has water. One delegate per field; the test drive and the match keep it.
    /// </summary>
    public static Func<float, float, float> SurfaceHeight(Battlefield field)
    {
        Tank.Sim.Terrain.HeightField terrain = field.Terrain;
        if (field.WaterLevel is not { } water) return terrain.HeightAt;
        return (x, z) => MathF.Max(terrain.HeightAt(x, z), water);
    }

    /// <summary>
    /// The catalog map whose ground has <paramref name="terrain"/>'s grid (the maps' grids all differ), or null: for a
    /// match joined before the room state names its map (the match start comes first, plan deviation 4).
    /// </summary>
    public static string? IdOfGrid(HeightField terrain)
    {
        if (terrain.SamplesX != terrain.SamplesZ) return null;
        if (terrain.SamplesX == RiverlandMap.Samples && terrain.CellSize == RiverlandMap.CellSize) return RiverlandMap.Id;
        if (terrain.SamplesX == SandboxMap.Samples && terrain.CellSize == SandboxMap.CellSize) return SandboxMap.Id;
        return null;
    }

    /// <summary>The site of map <paramref name="mapId"/> (ignoring case); the sandbox's for null, "" or an unknown id.</summary>
    public static MapSite For(string? mapId)
    {
        foreach (MapSite site in All)
            if (string.Equals(site.Map.Id, mapId, StringComparison.OrdinalIgnoreCase)) return site;
        return Sandbox;
    }
}
