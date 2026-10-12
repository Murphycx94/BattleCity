using Tank.Core.Math;
using Tank.Core.Rules;
using Tank.Core.World;

namespace Tank.Core.Tests;

public sealed class PowerUpTests
{
    private static (Simulation sim, int playerId) Setup(PowerUpType type, GameMode mode = GameMode.Coop)
    {
        var sim = new Simulation(new MatchConfig(mode, MapLibrary.Load(MapLibrary.DefaultFor(mode)), 5, EnemyTotal: 0));
        sim.AddPlayer(0, 0);
        TestWorld.StepIdle(sim);
        int id = sim.State.Players[0].TankId;
        var tank = TestWorld.Tank(sim, id);
        sim.State.PowerUps[sim.State.PowerUpCount++] = new PowerUpState
        {
            Id = sim.State.AllocateId(), Type = type, X = tank.X, Z = tank.Z, Ttl = 100,
        };
        return (sim, id);
    }

    [Fact]
    public void Star_UpgradesTank()
    {
        var (sim, id) = Setup(PowerUpType.Star);
        TestWorld.StepIdle(sim);
        Assert.Equal(1, TestWorld.Tank(sim, id).Stars);
        Assert.Equal(0, sim.State.PowerUpCount);
        Assert.Contains(sim.Events, e => e.Type == GameEventType.PowerUpTaken);
    }

    [Fact]
    public void Helmet_GivesLongShield()
    {
        var (sim, id) = Setup(PowerUpType.Helmet);
        TestWorld.StepIdle(sim);
        Assert.True(TestWorld.Tank(sim, id).ShieldTicks > TankStats.SpawnShieldTicks);
    }

    [Fact]
    public void Clock_FreezesOtherTeams()
    {
        var (sim, _) = Setup(PowerUpType.Clock);
        TestWorld.StepIdle(sim);
        Assert.True(sim.State.FreezeTicks[Teams.Enemy] > 0);
        Assert.Equal(0, sim.State.FreezeTicks[0]);
    }

    [Fact]
    public void Shovel_FortifiesEagleWithSteel_ThenRevertsToBrick()
    {
        var (sim, _) = Setup(PowerUpType.Shovel);
        var eagle = sim.State.Eagles[0];
        int cx = eagle.X / Units.Cell - 1, cz = eagle.Z / Units.Cell - 1; // diagonal ring cell above-left

        TestWorld.StepIdle(sim);
        Assert.Equal(CellType.Steel, sim.State.Map.Get(cx, cz));

        TestWorld.StepIdle(sim, TankStats.ShovelTicks);
        Assert.Equal(CellType.Brick, sim.State.Map.Get(cx, cz));
    }

    [Fact]
    public void Grenade_DestroysUnshieldedEnemies()
    {
        var (sim, _) = Setup(PowerUpType.Grenade);
        int e1 = TestWorld.AddTank(sim, TestWorld.Enemy(0, 0));
        var shielded = TestWorld.Enemy(1024, 0);
        shielded.ShieldTicks = 500;
        int e2 = TestWorld.AddTank(sim, shielded);

        TestWorld.StepIdle(sim);

        Assert.Equal(-1, sim.State.FindTankIndex(e1));
        Assert.NotEqual(-1, sim.State.FindTankIndex(e2));
    }

    [Fact]
    public void ExtraLife_AddsLife()
    {
        var (sim, _) = Setup(PowerUpType.ExtraLife);
        byte before = sim.State.Players[0].Lives;
        TestWorld.StepIdle(sim);
        Assert.Equal(before + 1, sim.State.Players[0].Lives);
    }

    [Fact]
    public void PowerUps_Expire()
    {
        var sim = TestWorld.CreateEmpty();
        sim.State.PowerUps[sim.State.PowerUpCount++] = new PowerUpState { Id = 99, X = 0, Z = 0, Ttl = 3 };
        TestWorld.StepIdle(sim, 3);
        Assert.Equal(0, sim.State.PowerUpCount);
    }
}
