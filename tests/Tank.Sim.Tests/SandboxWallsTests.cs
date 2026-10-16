using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class SandboxWallsTests
{
    private static readonly Battlefield Field = SandboxMap.Create();
    private static readonly Prop[] Walls = Field.Props.Where(p => p.IsWall || p.Kind == PropKind.Fence).ToArray();
    private static readonly Vector2 Yard = SandboxMap.TargetSpots[SandboxMap.YardTarget].Position;

    public static TheoryData<int> WallIds()
    {
        var data = new TheoryData<int>();
        foreach (Prop p in Walls) data.Add(p.Id);
        return data;
    }

    [Fact]
    public void The_yard_and_the_cover_hold_25_brick_9_stone_and_9_fence_segments()
    {
        Assert.Equal(25, Walls.Count(p => p.Kind == PropKind.BrickWall));
        Assert.Equal(9, Walls.Count(p => p.Kind == PropKind.StoneWall));
        Assert.Equal(9, Walls.Count(p => p.Kind == PropKind.Fence));
    }

    [Fact]
    public void Wall_ids_follow_on_from_the_bushes()
    {
        int lastBush = Field.Props.Where(p => p.Kind == PropKind.Bush).Max(p => p.Id);
        Assert.Equal(Enumerable.Range(lastBush + 1, Walls.Length), Walls.Select(p => p.Id));
        Assert.Equal(Field.Props.Count, Walls[^1].Id);
    }

    [Fact]
    public void No_two_segments_overlap()
    {
        for (int a = 0; a < Walls.Length; a++)
        for (int b = a + 1; b < Walls.Length; b++)
            Assert.False(Obb2.TryGetPenetration(Walls[a].Footprint, Walls[b].Footprint, out _), $"{Walls[a].Id} and {Walls[b].Id} overlap");
    }

    [Theory]
    [MemberData(nameof(WallIds))]
    public void Stands_on_flat_open_ground_clear_of_rocks_bushes_targets_the_spawn_and_the_lane(int id)
    {
        Prop wall = Field.PropById(id);
        Obb2 box = wall.Footprint;
        foreach (Prop p in Field.Props)
        {
            if (p.Kind == PropKind.Rock) Assert.False(Obb2.TryGetPenetration(box, p.Footprint, out _), $"on rock {p.Id}");
            if (p.Kind == PropKind.Bush) Assert.False(box.IntersectsCircle(p.Footprint.Center, p.Footprint.HalfExtents.X), $"in bush {p.Id}");
        }
        foreach (TargetSpot t in SandboxMap.TargetSpots)
            Assert.False(Obb2.TryGetPenetration(box, TankMotion.Footprint(t.Spec, t.Position, t.Yaw), out _), "on a target");
        Assert.False(Obb2.TryGetPenetration(box, TankMotion.Footprint(TankCatalog.Heavy, SandboxMap.Spawn, SandboxMap.SpawnYaw), out _));
        float lane = SandboxMap.DistanceToSegment(box.Center, SandboxMap.Spawn, SandboxMap.BigHill) - box.HalfExtents.Length();
        Assert.True(lane >= SandboxMap.LaneHalfWidth, $"{lane:F1} m from the lane");
        Assert.Equal(box.Center, Field.ClampToPlayable(box.Center));
        float slope = MathF.Acos(Field.Terrain.NormalAt(box.Center.X, box.Center.Y).Y);
        Assert.True(slope < Angles.Deg(8), $"{slope * 180 / MathF.PI:F1}° under it");
    }

    [Fact]
    public void The_yard_is_walled_in_but_for_the_fence_gate_on_its_far_side()
    {
        // From 1 m up at the yard target, under the fence tops, in every direction: the ray either meets a wall or leaves
        // through the gate, crossing a fence panel on the way.
        WallLine gate = SandboxMap.WallLines.Where(l => l.Kind == PropKind.Fence).MinBy(l => Vector2.Distance((l.Start + l.End) / 2, Yard));
        float gateX = gate.Start.X, gateSouth = MathF.Min(gate.Start.Y, gate.End.Y), gateNorth = MathF.Max(gate.Start.Y, gate.End.Y);
        Assert.Equal(gateX, gate.End.X);   // the gate runs north–south
        Prop[] fences = Walls.Where(p => p.Kind == PropKind.Fence).ToArray();
        var eye = new Vector3(Yard.X, Field.Terrain.HeightAt(Yard.X, Yard.Y) + 1f, Yard.Y);
        int throughTheGate = 0;
        for (int degrees = 0; degrees < 360; degrees += 5)
        {
            Vector2 d = Angles.Forward(Angles.Deg(degrees));
            Vector3 far = eye + new Vector3(d.X, 0, d.Y) * 30f;
            if (Field.Raycast(eye, far, out RayHit hit) && hit.PropId != RayHit.TerrainId && Field.PropById(hit.PropId).IsWall) continue;
            float z = Yard.Y + d.Y * (gateX - Yard.X) / d.X;   // where the ray crosses the gate's line
            Assert.True(d.X < 0 && z > gateSouth && z < gateNorth, $"the ray at {degrees}° gets out (crosses x = {gateX} at z = {z:F1})");
            Assert.True(fences.Any(f => Intersect.SegmentPrism(f.Footprint, f.Bottom, f.Top, eye, far, out _)), $"the ray at {degrees}° crosses no fence panel of the gate");
            throughTheGate++;
        }
        Assert.True(throughTheGate >= 3, $"only {throughTheGate} rays get out through the gate (fences don't stop rays, so at least 3 should)");
    }
}
