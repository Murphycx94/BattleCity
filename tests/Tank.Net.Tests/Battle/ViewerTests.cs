using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// <see cref="Viewer.Wants"/> on a crate picked (stage 6 design §4.4): nobody is sent the event — not the eater, whose own
/// viewer sees its own tank, not another seat's — and the one who ate it is told by a <see cref="CrateMessage"/> instead.
/// </summary>
public sealed class ViewerTests
{
    [Fact]
    public void Nobody_wants_a_crate_picked_event()
    {
        var sim = new Simulation(TestWorld.FlatField());
        int eater = sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0f, team: 0);
        int other = sim.AddTank(TankCatalog.Medium, new Vector2(110, 100), 0f, team: 0);
        var vision = new VisionTracker(sim);
        sim.Step(stackalloc InputCmd[] { BattleInputBuffer.Idle(1, default), BattleInputBuffer.Idle(1, default) });
        vision.Update();
        var picked = new GameEvent(GameEventType.CratePicked, sim.Tick, eater, 0, new Vector3(100, 0, 104), Vector3.Zero, Damage: 270, PropId: 3);
        var shot = picked with { Type = GameEventType.ShellFired, ShellId = 1 };

        foreach (Viewer viewer in new[]
                 {
                     new Viewer(0, eater, false, true),    // the eater's own
                     new Viewer(0, other, false, true),    // a seat that sees the eater (same team, 10 m off)
                     new Viewer(1, 0, true, true),         // a dead free-for-all watcher, who sees everything
                 })
        {
            Assert.False(viewer.Wants(picked, sim, vision, out GameEvent sent), $"{viewer} wants the pickup");
            Assert.Equal(default, sent);
            Assert.True(viewer.Wants(shot, sim, vision, out _), $"{viewer} doesn't even want a shot");   // the viewer is otherwise an ordinary one
        }
    }
}
