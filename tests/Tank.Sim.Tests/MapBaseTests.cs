using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Maps.Building;
using Tank.Sim.Modes;

namespace Tank.Sim.Tests;

/// <summary>
/// The two maps' team bases (stage 5 design §3.2): open flat ground behind each team's spawns, found with the riverland
/// probe's <c>bases</c> command; and <see cref="TeamBase.Check"/>.
/// </summary>
public sealed class MapBaseTests
{
    private const float R = TeamBase.StandardRadius;
    private const float Margin = 2f;                  // flat and free of houses, walls and trees this far past the rim
    private const float MinSpawnDistance = 40f, MaxSpawnDistance = 80f;
    private const float MaxOffBehindDegrees = 60f;
    private const float MaxSlopeDegrees = 8f, SampleStep = 2f;
    private const float CentreClearance = 12f;        // no rock, house, wall or fence footprint this near the centre
    private const int MaxRimRocks = 4;

    public static TheoryData<string> Maps => new() { "sandbox", "riverland" };

    private static readonly Lazy<Battlefield> LazySandbox = new(MapCatalog.Sandbox.NewBattlefield);
    private static readonly Lazy<Passability> LazySandboxPass = new(() => NewPassability(LazySandbox.Value));
    private static readonly Lazy<Passability> LazyRiverlandPass = new(() => NewPassability(RiverlandFixture.Field));

    private static Passability NewPassability(Battlefield field) => new(field, Angles.Deg(32), 1.6f);

    private static Battlefield FieldOf(string id) => id == RiverlandMap.Id ? RiverlandFixture.Field : LazySandbox.Value;

    private static Passability PassabilityOf(string id) => id == RiverlandMap.Id ? LazyRiverlandPass.Value : LazySandboxPass.Value;

    [Theory]
    [MemberData(nameof(Maps))]
    public void Each_team_has_one_base_of_radius_25(string id)
    {
        IReadOnlyList<TeamBase> bases = MapCatalog.Get(id).Bases;
        Assert.Equal(2, bases.Count);
        Assert.Single(bases, b => b.Team == 0);
        Assert.Single(bases, b => b.Team == 1);
        Assert.All(bases, b => Assert.Equal(25f, b.Radius));
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void Each_base_lies_40_to_80_m_behind_its_teams_spawns(string id)
    {
        MapDefinition map = MapCatalog.Get(id);
        foreach (TeamBase b in map.Bases)
        {
            Vector2[] own = Spawns(map, b.Team), enemy = Spawns(map, (byte)(1 - b.Team));
            Vector2 ownCentre = Average(own), enemyCentre = Average(enemy);
            float nearest = own.Min(s => Vector2.Distance(s, b.Centre));
            Assert.InRange(nearest, MinSpawnDistance, MaxSpawnDistance);
            float off = Degrees(MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(b.Centre - ownCentre), Vector2.Normalize(ownCentre - enemyCentre)), -1f, 1f)));
            Assert.True(off <= MaxOffBehindDegrees, $"team {b.Team}'s base at {b.Centre} is {off:F0}° off straight behind its spawns");
            Assert.True(Vector2.Distance(b.Centre, enemyCentre) > Vector2.Distance(b.Centre, ownCentre), $"team {b.Team}'s base at {b.Centre} is nearer the enemy");
        }
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void Each_base_is_flat_dry_ground_inside_the_playable_area(string id)
    {
        Battlefield field = FieldOf(id);
        float clear = R + Margin, minNormalY = MathF.Cos(Angles.Deg(MaxSlopeDegrees));
        foreach (TeamBase b in MapCatalog.Get(id).Bases)
        {
            Vector2 c = b.Centre;
            float edge = field.Border + clear;
            Assert.True(c.X >= edge && c.Y >= edge && c.X <= field.Terrain.SizeX - edge && c.Y <= field.Terrain.SizeZ - edge,
                $"team {b.Team}'s base at {c} is within {edge} m of the map's edge");
            for (float dz = -clear; dz <= clear; dz += SampleStep)
            for (float dx = -clear; dx <= clear; dx += SampleStep)
            {
                if (dx * dx + dz * dz > clear * clear) continue;
                var p = new Vector2(c.X + dx, c.Y + dz);
                float normalY = field.Terrain.NormalAt(p.X, p.Y).Y;
                Assert.True(normalY >= minNormalY, $"team {b.Team}'s base at {c}: {Degrees(MathF.Acos(normalY)):F1}° at {p}");
                Assert.True(field.WaterDepthAt(p) == 0, $"team {b.Team}'s base at {c}: water at {p}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void Each_base_is_clear_of_houses_walls_trees_and_bushes_with_at_most_four_rocks_at_its_rim(string id)
    {
        Battlefield field = FieldOf(id);
        foreach (TeamBase b in MapCatalog.Get(id).Bases)
        {
            Vector2 c = b.Centre;
            int rimRocks = 0;
            foreach (Prop p in field.Props)
            {
                float d = p.Footprint.DistanceToPoint(c);
                switch (p.Kind)
                {
                    case PropKind.Rock:
                        Assert.True(d >= CentreClearance, $"team {b.Team}'s base at {c}: rock {p.Id} {d:F1} m from the centre");
                        if (d < R + Margin) rimRocks++;
                        break;
                    case PropKind.House or PropKind.BrickWall or PropKind.StoneWall or PropKind.Fence:
                        Assert.True(d >= R + Margin, $"team {b.Team}'s base at {c}: {p.Kind} {p.Id} {d:F1} m from the centre");
                        break;
                    case PropKind.Tree:
                        float trunk = Vector2.Distance(p.Footprint.Center, c);
                        Assert.True(trunk >= R + Margin, $"team {b.Team}'s base at {c}: tree {p.Id} {trunk:F1} m from the centre");
                        break;
                    case PropKind.Bush:
                        float apart = Vector2.Distance(p.Footprint.Center, c);
                        Assert.True(apart >= R + p.Footprint.HalfExtents.X, $"team {b.Team}'s base at {c} overlaps bush {p.Id}");
                        break;
                }
            }
            Assert.True(rimRocks <= MaxRimRocks, $"team {b.Team}'s base at {c} has {rimRocks} rocks at its rim");
        }
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void Each_base_is_reachable_from_both_teams_spawns(string id)
    {
        MapDefinition map = MapCatalog.Get(id);
        Passability passable = PassabilityOf(id);
        foreach (TeamBase b in map.Bases)
        {
            Assert.True(passable.IsPassableAt(b.Centre), $"team {b.Team}'s base centre {b.Centre} is not passable");
            foreach (SpawnPoint s in map.Spawns.Team)
                Assert.True(passable.Connected(b.Centre, s.Position), $"team {b.Team}'s base at {b.Centre} can't be reached from {s.Position}");
        }
    }

    [Theory]
    [InlineData("sandbox", 330f, 400f)]
    [InlineData("riverland", 950f, 1020f)]
    public void The_bases_are_as_far_apart_as_measured(string id, float min, float max)
    {
        IReadOnlyList<TeamBase> bases = MapCatalog.Get(id).Bases;
        Assert.InRange(Vector2.Distance(bases[0].Centre, bases[1].Centre), min, max);
    }

    [Fact]
    public void Team_base_check_rejects_one_team_twice_a_third_base_and_bad_radii()
    {
        var a = new TeamBase(0, new Vector2(10, 10), R);
        var b = new TeamBase(1, new Vector2(90, 90), R);
        Assert.Throws<ArgumentException>("bases", () => TeamBase.Check(new[] { a, a with { Centre = new Vector2(20, 20) } }, "bases"));
        Assert.Throws<ArgumentException>("bases", () => TeamBase.Check(new[] { b, b }, "bases"));
        Assert.Throws<ArgumentException>("bases", () => TeamBase.Check(new[] { a, b, b with { Centre = new Vector2(50, 50) } }, "bases"));
        Assert.Throws<ArgumentException>("bases", () => TeamBase.Check(new[] { a }, "bases"));
        Assert.Throws<ArgumentException>("bases", () => TeamBase.Check(new[] { a, b with { Team = 2 } }, "bases"));
        Assert.Throws<ArgumentException>("bases", () => TeamBase.Check(new[] { a, b with { Radius = 0 } }, "bases"));
        Assert.Throws<ArgumentException>("bases", () => TeamBase.Check(new[] { a with { Radius = -1 }, b }, "bases"));
        Assert.Throws<ArgumentException>("bases", () => TeamBase.Check(new[] { a, b with { Radius = 101 } }, "bases"));
        Assert.Throws<ArgumentException>("bases", () => TeamBase.Check(new[] { a, b with { Radius = float.NaN } }, "bases"));
        Assert.Throws<ArgumentException>("bases", () => TeamBase.Check(new[] { a with { Centre = new Vector2(float.NaN, 0) }, b }, "bases"));
        Assert.Throws<ArgumentException>("bases", () => TeamBase.Check(new[] { a, b with { Centre = new Vector2(0, float.PositiveInfinity) } }, "bases"));
        Assert.Throws<ArgumentException>("spawns", () => TeamBase.Check(new[] { a }, "spawns"));

        Assert.Empty(TeamBase.Check(null, "bases"));
        Assert.Empty(TeamBase.Check(Array.Empty<TeamBase>(), "bases"));
        Assert.Equal(new[] { b, a }, TeamBase.Check(new[] { b, a }, "bases"));
        Assert.Equal(2, TeamBase.Check(new[] { a with { Radius = 100 }, b with { Radius = 0.5f } }, "bases").Count);
    }

    private static Vector2[] Spawns(MapDefinition map, byte team) =>
        map.Spawns.Team.Where(s => s.Team == team).Select(s => s.Position).ToArray();

    private static Vector2 Average(Vector2[] points) => points.Aggregate(Vector2.Zero, (sum, p) => sum + p) / points.Length;

    private static float Degrees(float radians) => radians * 180f / MathF.PI;
}
