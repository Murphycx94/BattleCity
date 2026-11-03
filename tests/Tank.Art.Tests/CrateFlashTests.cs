using System.Numerics;
using Tank.Client.World;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

/// <summary>
/// When a crate that goes from the world flashes (plan deviation 40): only when the player saw it eaten — it was in sight
/// the frame before (in the previous snapshot's rows) — or ate it; a spot re-seen empty, a crate a resync no longer
/// knows and the lot cleared on leaving go silently.
/// </summary>
public class CrateFlashTests
{
    private const byte Id = 4;
    private static readonly Crate Crate = new(Id, new Vector2(150.5f, 320.25f), CrateKind.Optics);

    private static BattleSnapshot Snapshot(params CrateView[] rows)
    {
        var s = new BattleSnapshot { Tick = 100 };
        for (int i = 0; i < rows.Length; i++) s.Crates[i] = rows[i];
        s.CrateCount = rows.Length;
        return s;
    }

    private static CrateView Row(bool standing) => new() { Id = Id, Kind = Crate.Kind, Standing = standing, X = 15050, Z = 32025 };

    /// <summary>A knowledge with the crate seen standing in the newest snapshot: in sight this frame.</summary>
    private static CrateKnowledge SeenStanding()
    {
        var crates = new CrateKnowledge();
        crates.Apply(Snapshot(Row(standing: true)));
        Assert.True(crates.All[Id].Visible);
        return crates;
    }

    [Fact]
    public void The_player_s_own_pickup_flashes()
    {
        var crates = new CrateKnowledge();
        crates.Reset(new[] { Crate });   // known from a resync, never in sight since
        bool wasVisible = crates.All[Id].Visible;

        crates.Take(Id, Crate.Kind, Crate.Position);

        Assert.True(CrateFlash.FlashesOnRemove(wasVisible, ownPickup: true, crates.All[Id]));
    }

    [Fact]
    public void A_crate_in_sight_seen_gone_in_the_next_snapshot_flashes()
    {
        CrateKnowledge crates = SeenStanding();
        bool wasVisible = crates.All[Id].Visible;

        crates.Apply(Snapshot(Row(standing: false)));

        Assert.True(CrateFlash.FlashesOnRemove(wasVisible, ownPickup: false, crates.All[Id]));
    }

    [Fact]
    public void An_empty_spot_seen_again_after_a_while_out_of_sight_does_not_flash()
    {
        CrateKnowledge crates = SeenStanding();
        crates.Apply(Snapshot());   // out of sight: still known standing
        bool wasVisible = crates.All[Id].Visible;
        Assert.False(wasVisible);

        crates.Apply(Snapshot(Row(standing: false)));   // someone ate it meanwhile; the spot comes back into sight empty

        Assert.False(CrateFlash.FlashesOnRemove(wasVisible, ownPickup: false, crates.All[Id]));
    }

    [Fact]
    public void A_resync_that_no_longer_knows_the_crate_does_not_flash()
    {
        CrateKnowledge crates = SeenStanding();
        bool wasVisible = crates.All[Id].Visible;

        crates.Reset(new[] { new Crate(9, new Vector2(700, 80), CrateKind.Repair) });

        Assert.False(CrateFlash.FlashesOnRemove(wasVisible, ownPickup: false, crates.All[Id]));
    }

    [Fact]
    public void Leaving_the_match_clears_the_crates_without_a_flash()
    {
        CrateKnowledge crates = SeenStanding();
        bool wasVisible = crates.All[Id].Visible;

        crates.Clear();

        Assert.False(CrateFlash.FlashesOnRemove(wasVisible, ownPickup: false, crates.All[Id]));
        Assert.False(CrateFlash.FlashesOnRemove(wasVisible, ownPickup: true, crates.All[Id]));
    }

    [Fact]
    public void A_crate_still_standing_never_flashes()
    {
        CrateKnowledge crates = SeenStanding();

        Assert.False(CrateFlash.FlashesOnRemove(wasVisible: true, ownPickup: true, crates.All[Id]));
    }
}
