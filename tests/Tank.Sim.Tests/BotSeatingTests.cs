using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// The server's seat rule (5C batch C review follow-up): one helper the server and the tests' whole matches both seat
/// with, so the tests prove what the server does. Bot seats draw their tanks in slot order from the match seed, every
/// seat goes on the thinner team, and each bot's seed is the match seed's mixed with its slot.
/// </summary>
public sealed class BotSeatingTests
{
    [Fact]
    public void Bot_seats_draw_their_tanks_from_the_match_seed_in_slot_order()
    {
        var seating = new BotSeating(5);
        var rng = new XorShift(5);
        for (int k = 0; k < 10; k++)
            Assert.Equal((int)(rng.NextUInt() % 4), seating.NextTankIndex());
    }

    [Fact]
    public void A_bots_seed_is_the_match_seed_mixed_with_its_slot()
    {
        var seating = new BotSeating(12345);
        Assert.Equal(12345u, seating.BotSeed(0));
        Assert.Equal(12345u ^ 7919u * 3, seating.BotSeed(3));
        Assert.Equal(BotSeating.SeedStride, 7919);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 3, 0)]
    [InlineData(4, 3, 1)]
    public void A_seat_goes_on_the_thinner_team_team_0_on_a_tie(int onTeam0, int onTeam1, byte team) =>
        Assert.Equal(team, BotSeating.ThinnerTeam(onTeam0, onTeam1));

    [Fact]
    public void Ten_bots_are_seated_alternating_teams()
    {
        BotArena arena = WholeMatch.Seat(MapCatalog.Sandbox, 7, recording: false);
        var rng = new XorShift(7);
        for (byte slot = 0; slot < WholeMatch.Seats; slot++)
        {
            int id = arena.Rules.Of(slot).TankId;
            TankState t = arena.Tank(id);
            Assert.Equal((byte)(slot % 2), t.Team);
            Assert.Same(TankCatalog.All[(int)(rng.NextUInt() % 4)], t.Spec);
            Assert.NotNull(arena.Director.Find(id));
        }
    }

    [Fact]
    public void Nine_bots_and_a_player_are_seated_as_the_server_seats_them()
    {
        // The player came into the room first (slot 0, team 0, the medium a seat starts with); the bots fill slots 1–9.
        BotArena arena = WholeMatch.Seat(MapCatalog.Sandbox, 7, recording: false, player: true);
        var rng = new XorShift(7);
        TankState player = arena.Tank(arena.Rules.Of(0).TankId);
        Assert.Equal(0, player.Team);
        Assert.Same(TankCatalog.Medium, player.Spec);
        Assert.Null(arena.Director.Find(player.Id));
        Assert.Equal(9, arena.Director.Bots.Count);
        for (byte slot = 1; slot < WholeMatch.Seats; slot++)
        {
            TankState t = arena.Tank(arena.Rules.Of(slot).TankId);
            Assert.Equal((byte)(slot % 2), t.Team);   // one player on team 0: the bots go 1, 0, 1, …
            Assert.Same(TankCatalog.All[(int)(rng.NextUInt() % 4)], t.Spec);
        }
    }
}
