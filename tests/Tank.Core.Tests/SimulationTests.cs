using Tank.Core.Math;
using Tank.Core.Rules;
using Tank.Core.World;

namespace Tank.Core.Tests;

public sealed class SimulationTests
{
    private static Simulation Coop(uint seed = 7, int enemies = 20, int players = 2)
    {
        var sim = new Simulation(new MatchConfig(GameMode.Coop, MapLibrary.Load("coop-01"), seed, EnemyTotal: enemies));
        for (int i = 0; i < players; i++) sim.AddPlayer(i, 0);
        return sim;
    }

    /// <summary>Scripted pseudo-random but reproducible inputs for both players.</summary>
    private static InputCmd[] ScriptedInputs(int tick)
    {
        var inputs = new InputCmd[GameState.MaxPlayers];
        for (int slot = 0; slot < inputs.Length; slot++)
        {
            int phase = (tick / 45 + slot * 3) % 5;
            inputs[slot] = new InputCmd(tick, (byte)phase, tick % 7 == slot, 2);
        }
        return inputs;
    }

    [Fact]
    public void SameSeedAndInputs_ProduceIdenticalStatesEveryTick()
    {
        var a = Coop();
        var b = Coop();
        for (int t = 1; t <= 3600; t++)
        {
            a.Step(ScriptedInputs(t));
            b.Step(ScriptedInputs(t));
            Assert.Equal(a.State.ComputeHash(), b.State.ComputeHash());
        }
        Assert.True(a.State.EnemiesSpawned > 0, "the run should have exercised enemy spawning");
    }

    [Fact]
    public void DifferentSeeds_Diverge()
    {
        var a = Coop(seed: 1);
        var b = Coop(seed: 2);
        for (int t = 1; t <= 600; t++)
        {
            a.Step(ScriptedInputs(t));
            b.Step(ScriptedInputs(t));
        }
        Assert.NotEqual(a.State.ComputeHash(), b.State.ComputeHash());
    }

    [Fact]
    public void Players_SpawnWithShield_OnFirstTick()
    {
        var sim = Coop();
        TestWorld.StepIdle(sim);

        int id = sim.State.Players[0].TankId;
        Assert.NotEqual(0, id);
        var tank = TestWorld.Tank(sim, id);
        Assert.True(tank.ShieldTicks > 0);
        Assert.True(tank.SpawnTicks > 0);
        Assert.Contains(sim.Events, e => e.Type == GameEventType.TankSpawned);
    }

    [Fact]
    public void Enemies_SpawnOverTime_UpToFieldLimit()
    {
        var sim = Coop(players: 1);
        sim.State.FreezeTicks[Teams.Enemy] = ushort.MaxValue; // keep them at spawn points out of each other's way
        TestWorld.StepIdle(sim, 3000);
        Assert.InRange(sim.State.AliveEnemyCount(), 1, TankStats.MaxEnemiesOnField(1));
    }

    [Fact]
    public void Coop_IsWon_WhenAllEnemiesAreDestroyed()
    {
        var sim = Coop(enemies: 1, players: 1);
        TestWorld.StepIdle(sim, 61);
        Assert.Equal(1, sim.State.AliveEnemyCount());

        // Grenade-equivalent: destroy the enemy directly.
        for (int i = 0; i < sim.State.TankCount; i++)
            if (!sim.State.Tanks[i].IsPlayer) sim.State.Tanks[i].Dead = true;
        TestWorld.StepIdle(sim);

        Assert.Equal(MatchPhase.Ended, sim.State.Phase);
        Assert.Equal(0, sim.State.WinnerTeam);
    }

    [Fact]
    public void Coop_IsLost_WhenEagleIsDestroyed()
    {
        var sim = Coop(players: 1);
        TestWorld.StepIdle(sim);
        var eagle = sim.State.Eagles[0];
        sim.State.Bullets[sim.State.BulletCount++] = new BulletState
        {
            Id = sim.State.AllocateId(), Team = Teams.Enemy, OwnerSlot = Teams.NoSlot, SpawnInputTick = -1,
            X = eagle.X + Units.Tile / 2, Z = eagle.Z + 30, Dir = Dir.Down, Speed = 32, // inside the brick ring
        };

        TestWorld.StepIdle(sim, 3);

        Assert.Equal(MatchPhase.Ended, sim.State.Phase);
        Assert.Equal(Teams.Enemy, sim.State.WinnerTeam);
        Assert.False(sim.State.Eagles[0].Alive);
    }

    [Fact]
    public void Coop_PlayerLosesLives_AndIsEliminatedAtZero()
    {
        var sim = Coop(players: 1);
        sim.State.Players[1] = default;
        for (int life = 0; life < 3; life++)
        {
            TestWorld.StepIdle(sim, TankStats.RespawnDelayTicks + 2);
            int id = sim.State.Players[0].TankId;
            Assert.NotEqual(0, id);
            Systems.CombatSystem.Destroy(sim, sim.State.FindTankIndex(id), Teams.NoSlot, awardScore: true);
        }
        Assert.True(sim.State.Players[0].Eliminated);
        TestWorld.StepIdle(sim);
        Assert.Equal(MatchPhase.Ended, sim.State.Phase);
    }

    [Fact]
    public void Deathmatch_PlayerRespawnsAfterDelay()
    {
        var sim = new Simulation(new MatchConfig(GameMode.Deathmatch, MapLibrary.Load("arena-01"), 3));
        sim.AddPlayer(0, 0);
        sim.AddPlayer(1, 1);
        TestWorld.StepIdle(sim);
        int first = sim.State.Players[0].TankId;
        Systems.CombatSystem.Destroy(sim, sim.State.FindTankIndex(first), 1, awardScore: true);
        Assert.Equal(1, sim.State.Players[1].Kills);

        TestWorld.StepIdle(sim, TankStats.RespawnDelayTicks + 1);

        Assert.NotEqual(0, sim.State.Players[0].TankId);
        Assert.NotEqual(first, sim.State.Players[0].TankId);
    }

    [Fact]
    public void Deathmatch_EndsAtScoreLimit()
    {
        var sim = new Simulation(new MatchConfig(GameMode.Deathmatch, MapLibrary.Load("arena-01"), 3, DeathmatchScoreLimit: 1));
        sim.AddPlayer(0, 0);
        sim.AddPlayer(1, 1);
        TestWorld.StepIdle(sim);
        Systems.CombatSystem.Destroy(sim, sim.State.FindTankIndex(sim.State.Players[1].TankId), 0, awardScore: true);
        TestWorld.StepIdle(sim);
        Assert.Equal(MatchPhase.Ended, sim.State.Phase);
        Assert.Equal(0, sim.State.WinnerTeam);
    }

    [Fact]
    public void Deathmatch_EndsWhenTimeRunsOut()
    {
        var sim = new Simulation(new MatchConfig(GameMode.Deathmatch, MapLibrary.Load("arena-01"), 3, DeathmatchTimeLimitTicks: 10));
        sim.AddPlayer(0, 0);
        TestWorld.StepIdle(sim, 11);
        Assert.Equal(MatchPhase.Ended, sim.State.Phase);
    }

    [Fact]
    public void BaseBattle_TeamLosesWhenItsEagleFalls()
    {
        var sim = new Simulation(new MatchConfig(GameMode.BaseBattle, MapLibrary.Load("versus-01"), 3));
        sim.AddPlayer(0, 0);
        sim.AddPlayer(1, 1);
        Assert.Equal(2, sim.State.EagleCount);
        int team1Eagle = sim.State.Eagles[0].Team == 1 ? 0 : 1;
        sim.State.Eagles[team1Eagle].Alive = false;

        TestWorld.StepIdle(sim);

        Assert.Equal(0, sim.State.WinnerTeam);
    }

    [Fact]
    public void RemovePlayer_RemovesTank()
    {
        var sim = Coop(players: 2);
        TestWorld.StepIdle(sim);
        int id = sim.State.Players[1].TankId;
        sim.RemovePlayer(1);
        Assert.Equal(-1, sim.State.FindTankIndex(id));
        Assert.False(sim.State.Players[1].Active);
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.AddPlayer(8, 0));
    }

    [Fact]
    public void EndedMatch_StopsSimulatingButKeepsTicking()
    {
        var sim = Coop(players: 1);
        sim.State.Phase = MatchPhase.Ended;
        uint before = sim.State.Map.ComputeChecksum();
        TestWorld.StepIdle(sim, 10);
        Assert.Equal(10, sim.State.Tick);
        Assert.Equal(0, sim.State.TankCount);
        Assert.Equal(before, sim.State.Map.ComputeChecksum());
    }

    [Fact]
    public void EnemyRoster_IsDeterministic_AndHasRequestedSize()
    {
        var a = EnemyRoster.ForStage(5, 20);
        Assert.Equal(20, a.Length);
        Assert.Equal(a, EnemyRoster.ForStage(5, 20));
        Assert.Contains(TankKind.Armor, a);
        Assert.Empty(EnemyRoster.ForStage(1, 0));
    }
}
