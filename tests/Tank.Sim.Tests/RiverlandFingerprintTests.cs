using Tank.Sim.Ai;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

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
        Allocation.AssertNone(() =>
        {
            BotArena arena = TenBots();
            arena.Run(300);
            Allocation.Window window = Allocation.Start();
            arena.Run(300);   // the director, the bots' commands, the step, the vision and the rules
            return window.Stop();
        });
    }

    /// <summary>
    /// A free-for-all on the riverland: ten tanks of every kind, each its own team, wandering between the map's waypoints
    /// under the <see cref="BotDirector"/> (seeds 1–10); the arena keeps no events.
    /// </summary>
    internal static BotArena TenBots()
    {
        var arena = new BotArena(BattleMode.FreeForAll, MapCatalog.Riverland) { Recording = false };
        TankSpec[] kinds = { TankCatalog.Medium, TankCatalog.Heavy, TankCatalog.Light, TankCatalog.Td };
        for (int k = 0; k < 10; k++) arena.Bot(kinds[k % kinds.Length], team: 0, seed: (uint)(k + 1));   // a free-for-all: each its own team
        return arena;
    }
}
