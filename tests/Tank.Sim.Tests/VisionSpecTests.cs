using System.Numerics;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class VisionSpecTests
{
    [Theory]
    [InlineData("light", 300f, 0.30f, 0.40f)]
    [InlineData("medium", 270f, 0.18f, 0.25f)]
    [InlineData("heavy", 250f, 0.08f, 0.12f)]
    [InlineData("td", 260f, 0.25f, 0.45f)]
    public void Each_tank_has_the_view_range_and_concealment_of_the_design(string id, float view, float moving, float still)
    {
        TankSpec spec = TankCatalog.ById(id);
        Assert.Equal(view, spec.ViewRange);
        Assert.Equal(new Concealment(moving, still), spec.Camo);
        Assert.InRange(spec.Camo.Moving, 0f, spec.Camo.Still);   // a moving tank is easier to spot
        Assert.InRange(spec.Camo.Still, 0f, 1f);
    }

    [Fact]
    public void A_tank_joins_the_team_it_is_added_to()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0f);
        sim.AddTank(TankCatalog.Light, new Vector2(120, 100), 0f, team: 1);
        Assert.Equal(0, sim.Tanks[0].Team);
        Assert.Equal(1, sim.Tanks[1].Team);
    }

    [Fact]
    public void A_team_past_the_last_is_refused()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(TankCatalog.Light, new Vector2(100, 100), 0f, team: SimConstants.MaxTeams - 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.AddTank(TankCatalog.Light, new Vector2(120, 100), 0f, team: SimConstants.MaxTeams));
        Assert.Equal(1, sim.TankCount);
    }

    [Fact]
    public void Firing_notes_the_tick_and_a_new_tank_has_not_fired()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0f);
        Assert.Equal(TankState.NeverFired, sim.Tanks[0].LastFired);
        var hold = new[] { TestWorld.Cmd() };
        for (int i = 0; i < 10; i++) sim.Step(hold);
        sim.Step(new[] { TestWorld.Cmd(fire: true) });
        Assert.Equal(sim.Tick, sim.Tanks[0].LastFired);
        sim.Step(hold);
        Assert.Equal(sim.Tick - 1, sim.Tanks[0].LastFired);
    }

    [Fact]
    public void Changing_tank_keeps_the_team_and_forgets_the_last_shot()
    {
        var sim = new Simulation(TestWorld.FlatField());
        int id = sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0f, team: 2);
        sim.Step(new[] { TestWorld.Cmd(fire: true) });
        Assert.NotEqual(TankState.NeverFired, sim.Tanks[0].LastFired);
        sim.ChangeSpec(id, TankCatalog.Heavy);
        Assert.Equal(2, sim.Tanks[0].Team);
        Assert.Equal(TankState.NeverFired, sim.Tanks[0].LastFired);
    }
}
