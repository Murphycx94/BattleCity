using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class TargetSpotTests
{
    private const int RockSpot = 4, FarSpot = 5;
    private const float GunHeight = 2.1f;   // the spawn tank's trunnion
    private const float PlateHeight = 1.0f;
    private static readonly Battlefield Field = SandboxMap.Create();
    private static readonly IReadOnlyList<TargetSpot> Spots = SandboxMap.TargetSpots;

    public static TheoryData<int> Indices() => new() { 0, 1, 2, 3, 4, 5 };

    private static Obb2 Hull(TargetSpot s) => TankMotion.Footprint(TankCatalog.Medium, s.Position, s.Yaw);

    private static IEnumerable<Vector2> Corners(Obb2 b)
    {
        foreach (int x in new[] { -1, 1 })
        foreach (int z in new[] { -1, 1 })
            yield return b.Center + b.AxisRight * (x * b.HalfExtents.X) + b.AxisForward * (z * b.HalfExtents.Y);
    }

    [Fact]
    public void ThereAreSix() => Assert.Equal(6, Spots.Count);

    [Theory]
    [MemberData(nameof(Indices))]
    public void StandsAtTheRightRange(int i)
    {
        float distance = Vector2.Distance(Spots[i].Position, SandboxMap.Spawn);
        if (i == FarSpot) Assert.InRange(distance, 180f, 200f);
        else Assert.InRange(distance, 30f, 200f);
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public void StandsOnFlatPlayableGround(int i)
    {
        Obb2 hull = Hull(Spots[i]);
        foreach (Vector2 p in Corners(hull).Append(hull.Center))
        {
            Assert.Equal(p, Field.ClampToPlayable(p));
            float slope = MathF.Acos(Field.Terrain.NormalAt(p.X, p.Y).Y);
            Assert.True(slope < Angles.Deg(8), $"spot {i}: {slope * 180 / MathF.PI:F1}° at {p}");
        }
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public void IsClearOfRocksBushesAndTheLane(int i)
    {
        Obb2 hull = Hull(Spots[i]);
        foreach (Prop p in Field.Props)
        {
            if (p.Kind == PropKind.Bush) Assert.False(hull.IntersectsCircle(p.Footprint.Center, p.Footprint.HalfExtents.X), $"spot {i} in bush {p.Id}");
            else Assert.False(Obb2.TryGetPenetration(hull, p.Footprint, out _), $"spot {i} on rock {p.Id}");
        }
        float lane = SandboxMap.DistanceToSegment(hull.Center, SandboxMap.Spawn, SandboxMap.BigHill) - hull.HalfExtents.Length();
        Assert.True(lane >= SandboxMap.LaneHalfWidth, $"spot {i} is {lane:F1} m from the lane");
        Obb2 spawn = TankMotion.Footprint(TankCatalog.Medium, SandboxMap.Spawn, SandboxMap.SpawnYaw);
        for (int j = 0; j < Spots.Count; j++)
            if (j != i) Assert.False(Obb2.TryGetPenetration(hull, Hull(Spots[j]), out _), $"spots {i} and {j} overlap");
        Assert.False(Obb2.TryGetPenetration(hull, spawn, out _));
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(1, 90f)]
    [InlineData(2, 45f)]
    [InlineData(3, 180f)]
    [InlineData(4, 0f)]
    [InlineData(5, 0f)]
    public void ShowsTheSpawnTheRightFace(int i, float degreesOffFront)
    {
        Vector2 toSpawn = Vector2.Normalize(SandboxMap.Spawn - Spots[i].Position);
        float off = MathF.Acos(Math.Clamp(Vector2.Dot(Angles.Forward(Spots[i].Yaw), toSpawn), -1f, 1f));
        Assert.Equal(Angles.Deg(degreesOffFront), off, 0.01f);
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public void CanBeSeenFromTheSpawn_ExceptTheRockOneIsHalfHidden(int i)
    {
        int clear = 0, rock = 0;
        foreach (RayHit? hit in SightLines(Spots[i]))
        {
            if (hit is null) clear++;
            else if (hit.Value.PropId != RayHit.TerrainId) rock++;
        }
        if (i == RockSpot)
        {
            Assert.True(clear >= 1 && rock >= 1, $"rock spot: {clear} clear, {rock} behind a rock");
        }
        else Assert.Equal(3, clear);
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public void NoOtherTargetStandsInTheWay(int i)
    {
        Vector3 eye = Eye();
        Vector3 aim = Plate(Spots[i].Position);
        for (int j = 0; j < Spots.Count; j++)
            if (j != i) Assert.False(Intersect.SegmentPrism(Hull(Spots[j]), -10, 10, eye, aim, out _), $"spot {j} blocks spot {i}");
    }

    /// <summary>Lines from the spawn gun to the left end, middle and right end of the hull as seen from the spawn; null when clear.</summary>
    private static IEnumerable<RayHit?> SightLines(TargetSpot spot)
    {
        Vector2 toSpawn = Vector2.Normalize(SandboxMap.Spawn - spot.Position);
        Vector2 across = new(-toSpawn.Y, toSpawn.X);
        foreach (float side in new[] { -1.6f, 0f, 1.6f })
            yield return Field.Raycast(Eye(), Plate(spot.Position + across * side), out RayHit hit) ? hit : null;
    }

    private static Vector3 Eye() => new(SandboxMap.Spawn.X, Field.Terrain.HeightAt(SandboxMap.Spawn.X, SandboxMap.Spawn.Y) + GunHeight, SandboxMap.Spawn.Y);

    private static Vector3 Plate(Vector2 p) => new(p.X, Field.Terrain.HeightAt(p.X, p.Y) + PlateHeight, p.Y);
}
