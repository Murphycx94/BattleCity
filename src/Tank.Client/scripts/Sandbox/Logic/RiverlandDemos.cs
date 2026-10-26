using Tank.Sim.Combat;
using Tank.Sim.Maps;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// The riverland's range demos (riverland design §6.8, <c>--map riverland --range-demo NAME</c>): shell a house down,
/// ram another one, push a heavy tank into the east forest's edge, wade the town's ford. Each is a <see cref="RangeDemo"/>
/// on <see cref="RiverlandMap.TargetSpots"/> that parks the player at a fixed <see cref="RangeDemo.Stand"/> and lays its
/// gun on (or drives towards) a fixed <see cref="RangeDemo.AimAt"/>; the targets only name the place. The points were
/// picked with the riverland probe (dev scripts: <c>demos</c> lists the town's houses and stands in front of them,
/// <c>sweep</c> and <c>fan</c> what a hull driving a line meets) and are pinned by <c>RiverlandDemoTests</c>, which run
/// each demo in the headless simulation.
/// </summary>
public static class RiverlandDemos
{
    /// <summary>
    /// The middle of the front (door) wall of the two-storey house at the north end of the town east of the river
    /// (prop 5176, its door facing east, nothing flimsy within 4 m of it): half its 5.6 m eaves above the ground there,
    /// so the shells strike the wall, not the roof.
    /// </summary>
    private static readonly SN.Vector3 ShelledWall = new(765.46f, 4.32f, 487.16f);

    /// <summary>
    /// 55 m straight out from <see cref="ShelledWall"/>, on open grass: nothing flimsy on the line of fire (a shell would
    /// knock it down first) and no tree's crown over the line from the camera behind the tank to the wall.
    /// </summary>
    private static readonly SN.Vector2 ShellingStand = new(819.40f, 497.89f);

    /// <summary>The middle of the farmhouse at the town's south-west corner, west of the river (prop 5184, its door facing west by south-west), halfway up.</summary>
    private static readonly SN.Vector3 RammedHouse = new(640.17f, 5.1f, 605.20f);

    /// <summary>
    /// 45 m straight out from <see cref="RammedHouse"/>'s door: nothing solid or flimsy on the way in or through it (so
    /// the house is the first thing to go down), no crown over the view.
    /// </summary>
    private static readonly SN.Vector2 RammingStand = new(594.41f, 589.12f);

    /// <summary>
    /// On the east-west road, 37 m back along it from where it enters the east forest
    /// (<see cref="RiverlandLandmarks.ForestEdge"/>), about as far short of the trees on the drive: of the stands 36–40 m
    /// back, the one where the headings half a degree either side of <see cref="ForestDepth"/>'s fell as many trees.
    /// </summary>
    private static readonly SN.Vector2 ForestStand = new(828.64f, 589.57f);

    /// <summary>
    /// 40 m into the east forest past its west edge, 2° north of east from <see cref="ForestStand"/>, north of the road
    /// (which runs clear through the trees): a heavy tank's 8 s drive fells three trunks under throttle and a fourth as
    /// it rolls to a stop, 30 m inside. Picked by driving every heading 5° either side of east from stands 36–40 m back
    /// in the headless simulation; most fell only two, the trees at the edge standing 5–10 m apart.
    /// </summary>
    private static readonly SN.Vector3 ForestDepth = new(904.0f, 5.7f, 586.94f);

    /// <summary>On the east-west road 40 m west of the town's ford (<see cref="RiverlandLandmarks.Ford"/>), short of the water.</summary>
    private static readonly SN.Vector2 FordStand = new(660f, 556f);

    /// <summary>
    /// The road on the far bank, 40 m east of the ford: the line there crosses the ford's middle, passing south of the
    /// light target standing side-on in it.
    /// </summary>
    private static readonly SN.Vector3 FarBank = new(740f, 1.3f, 566f);

    /// <summary>The demos' names, in the order <see cref="Find"/> documents them.</summary>
    public static IReadOnlyList<string> Names { get; } = new[] { "house", "ram-house", "trees", "ford" };

    /// <summary>
    /// house (HE from <see cref="ShellingStand"/> into <see cref="ShelledWall"/> whenever loaded, until the house is down:
    /// at most three medium-tank rounds, about 12 s in); ram-house (6 s at full throttle from <see cref="RammingStand"/>
    /// into <see cref="RammedHouse"/>: a medium tank is over its house ram speed when it hits, knocks the house down whole
    /// and drives on into the ruin); trees (8 s at full throttle from <see cref="ForestStand"/> towards
    /// <see cref="ForestDepth"/>: meant for <c>--tank heavy</c>, it fells trees and comes to rest in the forest); ford (5 s at
    /// full throttle from <see cref="FordStand"/> towards <see cref="FarBank"/>: a medium tank is in the middle of the
    /// river 4.5 s in); null for any other name. Each drives, or shoots, with whatever tank --tank gives the player.
    /// </summary>
    public static RangeDemo? Find(string? name) => name switch
    {
        "house" => At(RiverlandMap.HouseTarget, ShellingStand, ShelledWall, AmmoType.HE, keepFiring: true),
        "ram-house" => At(RiverlandMap.HouseTarget, RammingStand, RammedHouse, AmmoType.AP, keepFiring: false, driveSeconds: 6f),
        "trees" => At(RiverlandMap.ForestTarget, ForestStand, ForestDepth, AmmoType.AP, keepFiring: false, driveSeconds: 8f),
        "ford" => At(RiverlandMap.FordTarget, FordStand, FarBank, AmmoType.AP, keepFiring: false, driveSeconds: 5f),
        _ => null,
    };

    /// <summary>A demo near target <paramref name="target"/> that parks at <paramref name="stand"/> and aims at, or drives towards, <paramref name="aimAt"/>.</summary>
    private static RangeDemo At(int target, SN.Vector2 stand, SN.Vector3 aimAt, AmmoType ammo, bool keepFiring, float driveSeconds = 0f) =>
        new(target, 0f, 0f, DemoAim.HullFront, ammo, keepFiring, DriveSeconds: driveSeconds, Stand: stand, Spots: RiverlandMap.TargetSpots, AimAt: aimAt);
}
