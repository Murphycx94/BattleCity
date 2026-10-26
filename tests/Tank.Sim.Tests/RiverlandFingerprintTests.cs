using Tank.Sim.Ai;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>
/// Pins the riverland (riverland design §3.12) by its <see cref="MapFingerprint"/>, quantised so it holds on Apple silicon
/// and Intel alike: whoever changes its generation sees this fail, and updates the constant saying why in the commit
/// message. And the battle on it allocates nothing per tick (§4.1).
/// </summary>
public sealed class RiverlandFingerprintTests
{
    [Fact]
    public void Riverland_is_what_it_was()
    {
        Battlefield field = RiverlandMap.Create();

        Assert.Equal(0x16843D54D54128E4UL, MapFingerprint.Of(field));
        Assert.Equal(5560, field.Props.Count);
        Assert.Equal(606, field.Props.Count(p => p.Kind == PropKind.Rock));
        Assert.Equal(1041, field.Props.Count(p => p.Kind == PropKind.Bush));
        Assert.Equal(3516, field.Props.Count(p => p.Kind == PropKind.Tree));
        Assert.Equal(59, field.Props.Count(p => p.Kind == PropKind.House));
        Assert.Equal(42, field.Props.Count(p => p.Kind is PropKind.BrickWall or PropKind.StoneWall));
        Assert.Equal(296, field.Props.Count(p => p.Kind == PropKind.Fence));
    }

    [Fact]
    public void Stepping_ten_tanks_on_riverland_allocates_nothing()
    {
        var (sim, vision, bots) = TenBots();
        var inputs = new InputCmd[sim.TankCount];
        void Tick()
        {
            for (int k = 0; k < bots.Length; k++) inputs[k] = bots[k].Command(sim, vision, sim.Tick + 1);
            sim.Step(inputs);
            vision.Update();
        }
        for (int i = 0; i < 300; i++) Tick();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++) Tick();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    /// <summary>Ten tanks of every kind on the first ten free-for-all spawns, each its own team, driven by skirmish bots.</summary>
    internal static (Simulation Sim, VisionTracker Vision, SkirmishBot[] Bots) TenBots()
    {
        var sim = new Simulation(MapCatalog.Riverland.NewBattlefield());
        TankSpec[] kinds = { TankCatalog.Medium, TankCatalog.Heavy, TankCatalog.Light, TankCatalog.Td };
        var bots = new SkirmishBot[10];
        for (int k = 0; k < bots.Length; k++)
        {
            SpawnPoint spawn = RiverlandMap.Spawns.Solo[k];
            int id = sim.AddTank(kinds[k % kinds.Length], spawn.Position, spawn.Yaw, team: (byte)k);
            bots[k] = new SkirmishBot(id, RiverlandMap.Waypoints, seed: (uint)(k + 1));
        }
        return (sim, new VisionTracker(sim), bots);
    }
}
