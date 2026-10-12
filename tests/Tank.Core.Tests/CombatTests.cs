using Tank.Core.Math;
using Tank.Core.Rules;
using Tank.Core.World;

namespace Tank.Core.Tests;

public sealed class CombatTests
{
    private static Simulation SimWithPlayer(out int playerId, int x = 1024, int z = 1024, Dir dir = Dir.Up, string? map = null)
    {
        var sim = map is null ? TestWorld.CreateEmpty(24, 24) : TestWorld.Create(map);
        sim.State.Players[0] = new PlayerState { Active = true, Team = 0, Lives = 3 };
        playerId = TestWorld.AddTank(sim, TestWorld.Player(0, x, z, dir));
        sim.State.Players[0].TankId = playerId;
        return sim;
    }

    private static void Fire(Simulation sim, byte viewDelta = 0) =>
        sim.Step(TestWorld.Inputs(sim.State.Tick + 1, fire: true, viewDelta: viewDelta));

    [Fact]
    public void Fire_SpawnsBulletAtMuzzle_WithPredictionTag()
    {
        var sim = SimWithPlayer(out int id);

        Fire(sim);

        var b = sim.State.Bullets[0];
        Assert.Equal(1, sim.State.BulletCount);
        Assert.Equal(id, b.OwnerTankId);
        Assert.Equal(Dir.Up, b.Dir);
        Assert.Equal(sim.State.Tick, b.SpawnInputTick);
        Assert.Contains(sim.Events, e => e.Type == GameEventType.Fired);
    }

    [Fact]
    public void Fire_RespectsMaxBulletsAndCooldown()
    {
        var sim = SimWithPlayer(out _);
        for (int i = 0; i < 20; i++) Fire(sim);
        Assert.Equal(1, sim.State.BulletCount); // 0 stars: one bullet in flight
    }

    [Fact]
    public void TwoStarTank_CanHaveTwoBullets()
    {
        var sim = SimWithPlayer(out int id);
        TestWorld.Tank(sim, id).Stars = 2;
        for (int i = 0; i < 20; i++) Fire(sim);
        Assert.Equal(2, sim.State.BulletCount);
    }

    [Fact]
    public void Bullet_DestroysBrickStripFourCellsWide_OneDeep()
    {
        var sim = SimWithPlayer(out _, x: 1024, z: 1024);
        var map = sim.State.Map;
        for (int cz = 8; cz <= 10; cz++)
            for (int cx = 0; cx < map.WidthCells; cx++)
                map.Set(cx, cz, CellType.Brick);

        Fire(sim);
        TestWorld.StepIdle(sim, 60);

        int centerCell = (1024 + Units.TankSize / 2) / Units.Cell; // 18
        Assert.Equal(0, sim.State.BulletCount);
        for (int cx = centerCell - 2; cx < centerCell + 2; cx++) Assert.Equal(CellType.Empty, map.Get(cx, 10));
        Assert.Equal(CellType.Brick, map.Get(centerCell - 3, 10));
        Assert.Equal(CellType.Brick, map.Get(centerCell + 2, 10));
        Assert.Equal(CellType.Brick, map.Get(centerCell, 9));
    }

    [Fact]
    public void NormalBullet_DoesNotDamageSteel_PowerBulletDoes()
    {
        foreach (bool power in new[] { false, true })
        {
            var sim = SimWithPlayer(out int id);
            if (power) TestWorld.Tank(sim, id).Stars = 3;
            var map = sim.State.Map;
            for (int cx = 0; cx < map.WidthCells; cx++) { map.Set(cx, 9, CellType.Steel); map.Set(cx, 10, CellType.Steel); }

            Fire(sim);
            TestWorld.StepIdle(sim, 60);

            int c = (1024 + Units.TankSize / 2) / Units.Cell;
            var expected = power ? CellType.Empty : CellType.Steel;
            Assert.Equal(expected, map.Get(c, 10));
            Assert.Equal(expected, map.Get(c, 9));
        }
    }

    [Fact]
    public void Bullet_KillsEnemy_AndAwardsScore()
    {
        var sim = SimWithPlayer(out _);
        TestWorld.AddTank(sim, TestWorld.Enemy(1024, 300, TankKind.Fast));
        sim.State.FreezeTicks[Teams.Enemy] = 10_000;

        Fire(sim);
        TestWorld.StepIdle(sim, 60);

        Assert.Equal(0, sim.State.AliveEnemyCount());
        Assert.Equal(TankStats.KillScore(TankKind.Fast), sim.State.Players[0].Score);
        Assert.Equal(1, sim.State.Players[0].Kills);
    }

    [Fact]
    public void ArmorTank_NeedsFourHits()
    {
        var sim = SimWithPlayer(out _);
        int enemy = TestWorld.AddTank(sim, TestWorld.Enemy(1024, 300, TankKind.Armor));
        sim.State.FreezeTicks[Teams.Enemy] = 10_000; // keep it still

        for (int shot = 0; shot < 3; shot++)
        {
            Fire(sim);
            TestWorld.StepIdle(sim, 60);
        }
        Assert.Equal(1, TestWorld.Tank(sim, enemy).Hp);

        Fire(sim);
        TestWorld.StepIdle(sim, 60);
        Assert.Equal(-1, sim.State.FindTankIndex(enemy));
    }

    [Fact]
    public void Shield_AbsorbsHit()
    {
        var sim = SimWithPlayer(out _);
        var enemy = TestWorld.Enemy(1024, 300);
        enemy.ShieldTicks = 1000;
        int id = TestWorld.AddTank(sim, enemy);
        sim.State.FreezeTicks[Teams.Enemy] = 10_000;

        Fire(sim);
        TestWorld.StepIdle(sim, 60);

        Assert.NotEqual(-1, sim.State.FindTankIndex(id));
        Assert.Equal(0, sim.State.BulletCount);
    }

    [Fact]
    public void OpposingBullets_CancelEachOther()
    {
        var sim = SimWithPlayer(out _);
        var enemy = TestWorld.Enemy(1024, 100, dir: Dir.Down);
        int enemyId = TestWorld.AddTank(sim, enemy);
        sim.State.Bullets[sim.State.BulletCount++] = new BulletState
        {
            Id = sim.State.AllocateId(), OwnerTankId = enemyId, OwnerSlot = Teams.NoSlot, Team = Teams.Enemy,
            X = 1024 + 128, Z = 400, Dir = Dir.Down, Speed = 32, SpawnInputTick = -1,
        };
        sim.State.FreezeTicks[Teams.Enemy] = 10_000;

        Fire(sim);
        TestWorld.StepIdle(sim, 40);

        Assert.Equal(0, sim.State.BulletCount);
        Assert.NotEqual(-1, sim.State.FindTankIndex(enemyId));
        Assert.NotEqual(0, sim.State.Players[0].TankId);
    }

    [Fact]
    public void FriendlyBullets_PassThroughTeammates()
    {
        var sim = SimWithPlayer(out _);
        sim.State.Players[1] = new PlayerState { Active = true, Team = 0, Lives = 3 };
        int mate = TestWorld.AddTank(sim, TestWorld.Player(1, 1024, 300, team: 0));
        sim.State.Players[1].TankId = mate;

        Fire(sim);
        TestWorld.StepIdle(sim, 60);

        Assert.NotEqual(-1, sim.State.FindTankIndex(mate));
    }

    [Fact]
    public void BonusTank_DropsPowerUpWhenHit()
    {
        var sim = SimWithPlayer(out _);
        var enemy = TestWorld.Enemy(1024, 300, TankKind.Armor);
        enemy.Bonus = true;
        TestWorld.AddTank(sim, enemy);
        sim.State.FreezeTicks[Teams.Enemy] = 10_000;

        Fire(sim);
        TestWorld.StepIdle(sim, 60);

        Assert.Equal(1, sim.State.PowerUpCount);
    }

    [Fact]
    public void LagCompensation_HitsTankWhereShooterSawIt()
    {
        // The enemy has just left the bullet's path on the server, but the shooter (rendering 4 ticks
        // in the past) still saw it there when firing.
        var sim = SimWithPlayer(out _);
        int enemy = TestWorld.AddTank(sim, TestWorld.Enemy(1024, 728));
        sim.State.FreezeTicks[Teams.Enemy] = 10_000;
        TestWorld.StepIdle(sim, 4);                 // history now holds the enemy at x=1024
        TestWorld.Tank(sim, enemy).X = 1024 + 600;   // moved away on the server

        Fire(sim, viewDelta: 4);
        TestWorld.StepIdle(sim, 30);

        Assert.Equal(-1, sim.State.FindTankIndex(enemy));
    }

    [Fact]
    public void WithoutLagCompensation_SameShotMisses()
    {
        var sim = SimWithPlayer(out _);
        int enemy = TestWorld.AddTank(sim, TestWorld.Enemy(1024, 728));
        sim.State.FreezeTicks[Teams.Enemy] = 10_000;
        TestWorld.StepIdle(sim, 4);
        TestWorld.Tank(sim, enemy).X = 1024 + 600;

        Fire(sim, viewDelta: 0);
        TestWorld.StepIdle(sim, 30);

        Assert.NotEqual(-1, sim.State.FindTankIndex(enemy));
    }
}
