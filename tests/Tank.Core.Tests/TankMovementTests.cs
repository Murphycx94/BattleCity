using Tank.Core.Math;
using Tank.Core.Rules;
using Tank.Core.Systems;
using Tank.Core.World;

namespace Tank.Core.Tests;

public sealed class TankMovementTests
{
    private static readonly GameMap Open = new(32, 32);

    private static TankState At(int x, int z, Dir dir = Dir.Up) => TestWorld.Player(0, x, z, dir);

    private static InputCmd Move(Dir d) => new(1, InputCmd.Encode(d), false);

    [Fact]
    public void Step_MovesBySpeed_InInputDirection()
    {
        var t = At(512, 512);

        TankMovement.Step(ref t, Move(Dir.Up), false, Open, ReadOnlySpan<Box>.Empty);

        Assert.Equal(512 - TankStats.Speed(t), t.Z);
        Assert.Equal(512, t.X);
        Assert.True(t.Moving);
        Assert.False(t.Blocked);
    }

    [Fact]
    public void Step_WithoutInput_StaysPut()
    {
        var t = At(512, 512);
        TankMovement.Step(ref t, InputCmd.Idle(1), false, Open, ReadOnlySpan<Box>.Empty);
        Assert.Equal((512, 512), (t.X, t.Z));
        Assert.False(t.Moving);
    }

    [Fact]
    public void PerpendicularTurn_SnapsToHalfTileGrid()
    {
        var t = At(512, 512 + 70, Dir.Up); // 70 sub past the grid: nearest half line is 512+128

        TankMovement.Step(ref t, Move(Dir.Right), false, Open, ReadOnlySpan<Box>.Empty);

        Assert.Equal(512 + Units.Half, t.Z);
        Assert.Equal(Dir.Right, t.Dir);
    }

    [Fact]
    public void ReverseTurn_DoesNotSnap()
    {
        var t = At(512, 530, Dir.Up);
        TankMovement.Step(ref t, Move(Dir.Down), false, Open, ReadOnlySpan<Box>.Empty);
        Assert.Equal(530 + TankStats.Speed(t), t.Z);
    }

    [Fact]
    public void Turn_DoesNotSnapIntoWall()
    {
        var map = new GameMap(32, 32);
        // Wall directly below where snapping would push the tank.
        for (int cx = 0; cx < 32; cx++) map.Set(cx, (512 + Units.Tile + Units.Half) / Units.Cell - 1, CellType.Steel);
        var t = At(512, 512 + 70, Dir.Up);

        TankMovement.Step(ref t, Move(Dir.Right), false, map, ReadOnlySpan<Box>.Empty);

        Assert.Equal(512 + 70, t.Z);
        Assert.Equal(Dir.Right, t.Dir);
    }

    [Fact]
    public void Movement_StopsFlushAgainstBrick()
    {
        var map = new GameMap(32, 32);
        int wallRow = 4; // wall occupies z in [256, 320)
        for (int cx = 0; cx < 32; cx++) map.Set(cx, wallRow, CellType.Brick);
        var t = At(512, 320 + 5, Dir.Up); // 5 sub below the wall

        TankMovement.Step(ref t, Move(Dir.Up), false, map, ReadOnlySpan<Box>.Empty);

        Assert.Equal(320, t.Z);
        Assert.True(t.Blocked);

        TankMovement.Step(ref t, Move(Dir.Up), false, map, ReadOnlySpan<Box>.Empty);
        Assert.Equal(320, t.Z);
    }

    [Fact]
    public void Movement_CannotLeaveMap()
    {
        var t = At(0, 4, Dir.Up);
        TankMovement.Step(ref t, Move(Dir.Up), false, Open, ReadOnlySpan<Box>.Empty);
        Assert.Equal(0, t.Z);
    }

    [Theory]
    [InlineData(CellType.Water, true)]
    [InlineData(CellType.Steel, true)]
    [InlineData(CellType.Forest, false)]
    [InlineData(CellType.Ice, false)]
    public void Terrain_TankPassability(CellType type, bool blocks)
    {
        var map = new GameMap(32, 32);
        map.Set(8, 7, type);
        var t = At(512, 512, Dir.Up); // cell row 7 is directly above (z 448..512)
        TankMovement.Step(ref t, Move(Dir.Up), false, map, ReadOnlySpan<Box>.Empty);
        Assert.Equal(blocks, t.Z == 512);
    }

    [Fact]
    public void Movement_StopsFlushAgainstOtherTank()
    {
        var t = At(512, 512, Dir.Right);
        Box other = new(512 + Units.TankSize + 5, 512, Units.TankSize, Units.TankSize);

        TankMovement.Step(ref t, Move(Dir.Right), false, Open, new[] { other });

        Assert.Equal(512 + 5, t.X);
        Assert.True(t.Blocked);
    }

    [Fact]
    public void Movement_IgnoresObstacleItAlreadyOverlaps()
    {
        var t = At(512, 512, Dir.Right);
        Box overlapping = new(600, 512, Units.TankSize, Units.TankSize);
        TankMovement.Step(ref t, Move(Dir.Left), false, Open, new[] { overlapping });
        Assert.Equal(512 - TankStats.Speed(t), t.X);
    }

    [Fact]
    public void SpawningOrFrozenTanks_DoNotMove()
    {
        var spawning = At(512, 512);
        spawning.SpawnTicks = 5;
        TankMovement.Step(ref spawning, Move(Dir.Up), false, Open, ReadOnlySpan<Box>.Empty);
        Assert.Equal(512, spawning.Z);

        var frozen = At(512, 512);
        TankMovement.Step(ref frozen, Move(Dir.Up), true, Open, ReadOnlySpan<Box>.Empty);
        Assert.Equal(512, frozen.Z);
    }

    [Fact]
    public void Ice_KeepsSlidingAfterRelease_ThenStops()
    {
        var map = new GameMap(32, 32);
        for (int cz = 0; cz < 32; cz++)
            for (int cx = 0; cx < 32; cx++)
                map.Set(cx, cz, CellType.Ice);
        var t = At(512, 1024, Dir.Up);

        TankMovement.Step(ref t, Move(Dir.Up), false, map, ReadOnlySpan<Box>.Empty);
        int afterPress = t.Z;
        for (int i = 0; i < TankStats.IceSlideTicks; i++)
            TankMovement.Step(ref t, InputCmd.Idle(1), false, map, ReadOnlySpan<Box>.Empty);
        int afterSlide = t.Z;
        TankMovement.Step(ref t, InputCmd.Idle(1), false, map, ReadOnlySpan<Box>.Empty);

        Assert.Equal(afterPress - TankStats.IceSlideTicks * TankStats.Speed(t), afterSlide);
        Assert.Equal(afterSlide, t.Z);
    }

    [Fact]
    public void EnemySpeeds_DifferByKind()
    {
        Assert.True(TankStats.Speed(TestWorld.Enemy(0, 0, TankKind.Fast)) > TankStats.Speed(TestWorld.Enemy(0, 0, TankKind.Basic)));
        Assert.Equal(4, TankStats.InitialHp(TankKind.Armor));
    }
}
