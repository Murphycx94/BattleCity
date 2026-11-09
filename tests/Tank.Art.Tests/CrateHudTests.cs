using Tank.Client.Match;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Testing;

namespace Tank.Art.Tests;

public class CrateHudTests
{
    [Theory]
    [InlineData(CrateKind.Repair, "维修")]
    [InlineData(CrateKind.Ammo, "弹药")]
    [InlineData(CrateKind.Armor, "加固装甲")]
    [InlineData(CrateKind.Reload, "快速装填")]
    [InlineData(CrateKind.Optics, "光学设备")]
    public void Every_kind_has_its_name(CrateKind kind, string name) => Assert.Equal(name, CrateText.Name(kind));

    [Theory]
    [InlineData(CrateKind.Repair, 270, 0, 0, "+ 维修 270")]
    [InlineData(CrateKind.Ammo, 20, 8, 0, "+ 弹药 穿甲 20 · 高爆 8")]
    [InlineData(CrateKind.Ammo, 0, 8, 0, "+ 弹药 高爆 8")]
    [InlineData(CrateKind.Ammo, 13, 0, 0, "+ 弹药 穿甲 13")]
    [InlineData(CrateKind.Reload, 1, 0, 2, "+ 快速装填 2/3")]
    [InlineData(CrateKind.Armor, 1, 0, 1, "+ 加固装甲 1/3")]
    [InlineData(CrateKind.Optics, 0, 0, 3, "光学设备已满")]
    [InlineData(CrateKind.Repair, 0, 0, 0, "维修已满")]
    [InlineData(CrateKind.Ammo, 0, 0, 0, "弹药已满")]
    public void A_pickup_reads_what_it_did_or_that_it_was_full(CrateKind kind, int amount, int amount2, byte layers, string text)
    {
        Assert.Equal(text, CrateText.Pickup(kind, amount, amount2, layers));
        Assert.Equal(text, new PickupLine(kind, amount, amount2, layers, ExpiresAt: 1).Text);
    }

    [Fact]
    public void Layers_read_as_a_times_sign_and_the_count()
    {
        Assert.Equal("×2", CrateText.Layers(2));
        Assert.Equal("×1", CrateText.Layers(1));
        Assert.Equal("×3", CrateText.Layers(3));
    }

    [Fact]
    public void The_buffs_come_from_the_tank_s_three_layers()
    {
        var tank = new TankState { Spec = TankCatalog.Medium, ArmorLayers = 1, ReloadLayers = 2, OpticsLayers = 3 };

        BuffHud buffs = BuffHud.From(tank);

        Assert.Equal(new BuffHud(1, 2, 3), buffs);
        Assert.True(buffs.Any);
        Assert.False(BuffHud.From(new TankState { Spec = TankCatalog.Medium }).Any);
        Assert.False(default(BuffHud).Any);
        Assert.True(new BuffHud(0, 0, 1).Any);
    }

    [Fact]
    public void The_feed_keeps_the_three_newest_and_drops_the_oldest()
    {
        var feed = new PickupFeed();
        feed.Add(CrateKind.Repair, 270, 0, 0, now: 0);
        feed.Add(CrateKind.Ammo, 20, 8, 0, now: 0.1);
        feed.Add(CrateKind.Armor, 1, 0, 1, now: 0.2);
        feed.Add(CrateKind.Optics, 1, 0, 1, now: 0.3);

        ReadOnlySpan<PickupLine> lines = feed.Visible(0.5);

        Assert.Equal(PickupFeed.Capacity, lines.Length);
        Assert.Equal(CrateKind.Ammo, lines[0].Kind);     // the repair went
        Assert.Equal(CrateKind.Optics, lines[2].Kind);   // newest last
        Assert.Equal(0.3 + PickupFeed.Lifetime, lines[2].ExpiresAt, precision: 9);
    }

    [Fact]
    public void A_line_shows_for_two_and_a_half_seconds()
    {
        var feed = new PickupFeed();
        feed.Add(CrateKind.Reload, 1, 0, 2, now: 10);
        feed.Add(CrateKind.Repair, 270, 0, 0, now: 11);

        Assert.Equal(2, feed.Visible(12.49).Length);
        ReadOnlySpan<PickupLine> later = feed.Visible(12.5);
        Assert.Equal(1, later.Length);
        Assert.Equal(CrateKind.Repair, later[0].Kind);
        Assert.True(feed.Visible(13.5).IsEmpty);
    }

    [Fact]
    public void The_revision_moves_with_every_add_and_every_expiry_and_clear_empties_the_feed()
    {
        var feed = new PickupFeed();
        int r0 = feed.Revision;
        feed.Add(CrateKind.Repair, 270, 0, 0, now: 0);
        int r1 = feed.Revision;
        feed.Add(CrateKind.Repair, 270, 0, 0, now: 0);
        int r2 = feed.Revision;
        Assert.NotEqual(r0, r1);
        Assert.NotEqual(r1, r2);

        feed.Visible(1);
        Assert.Equal(r2, feed.Revision);   // nothing expired
        feed.Visible(3);
        Assert.NotEqual(r2, feed.Revision);

        feed.Add(CrateKind.Ammo, 20, 8, 0, now: 3);
        int r3 = feed.Revision;
        feed.Clear();
        Assert.True(feed.Visible(3).IsEmpty);
        Assert.NotEqual(r3, feed.Revision);
    }

    [Fact]
    public void Reading_the_feed_allocates_nothing()
    {
        Allocation.AssertNone(() =>
        {
            var feed = new PickupFeed();
            for (int i = 0; i < 5; i++) feed.Add(CrateKind.Armor, 1, 0, 1, now: i * 0.5);
            feed.Visible(1);   // warm-up

            var window = Allocation.Start();
            int lines = 0;
            for (int i = 0; i < 100; i++)
            {
                feed.Add(CrateKind.Reload, 1, 0, 2, now: 2 + i * 0.1);
                lines += feed.Visible(2 + i * 0.1).Length;
            }
            var measured = window.Stop();
            Assert.True(lines > 0);
            return measured;
        });
    }
}
