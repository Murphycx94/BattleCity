using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>
/// Seeing a crate (stage 6 design §4.4): a medium tank looks north (−Z) at a crate straight ahead of it. Its crew sees the
/// crate's sign within the tank's view range (optics layers included) along a line from the turret top to the sign's top
/// that the ground, rocks, houses and standing walls can stop; fences, bushes and trees can't. A wreck sees nothing.
/// </summary>
public sealed class CrateSightTests
{
    /// <summary>On the 200 m field: the obstacle tests, the crate 60 m ahead.</summary>
    private static readonly Vector2 ObserverAt = new(100, 190);
    private static readonly Vector2 CrateAt = ObserverAt - new Vector2(0, 60);

    /// <summary>On the 400 m field: the range tests.</summary>
    private static readonly Vector2 FarObserverAt = new(200, 390);

    private static TankState Observer(Vector2 at, byte opticsLayers = 0) => new()
    {
        Id = 1, Spec = TankCatalog.Medium, Position = at, LastFired = TankState.NeverFired, OpticsLayers = opticsLayers,
    };

    private static bool Sees(Battlefield field, byte opticsLayers = 0) => CrateSight.Sees(Observer(ObserverAt, opticsLayers), CrateAt, field);

    /// <summary>A rock slab across the line of sight at <paramref name="z"/>, <paramref name="height"/> m tall.</summary>
    private static Prop Slab(int id, float z, float height) =>
        new(id, PropKind.Rock, new Obb2(new Vector2(100, z), new Vector2(6, 0.5f), 0), -1f, height + 1f);

    private static Prop Bush(int id, Vector2 at, float radius = 3.5f) =>
        new(id, PropKind.Bush, new Obb2(at, new Vector2(radius, radius), 0), -0.2f, 4.5f);

    /// <summary>A 2 m piece of <paramref name="kind"/> across the line of sight at <paramref name="z"/>.</summary>
    private static Prop Piece(PropKind kind, float z, int id) =>
        new WallLine(kind, new Vector2(99, z), new Vector2(101, z)).Segments(TestWorld.Flat(), id).Single();

    [Theory]
    [InlineData(200f, true)]
    [InlineData(270f, true)]    // the view range itself
    [InlineData(271f, false)]   // one metre past it
    public void In_the_open_a_crate_is_seen_out_to_the_view_range(float distance, bool seen)
    {
        Assert.Equal(270f, TankCatalog.Medium.ViewRange);
        Assert.Equal(seen, CrateSight.Sees(Observer(FarObserverAt), FarObserverAt - new Vector2(0, distance), TestWorld.WideFlatField()));
    }

    [Fact]
    public void An_optics_layer_lets_a_medium_tank_see_a_crate_290_m_away()
    {
        Battlefield field = TestWorld.WideFlatField();
        Vector2 crate = FarObserverAt - new Vector2(0, 290);
        Assert.False(CrateSight.Sees(Observer(FarObserverAt), crate, field));
        Assert.True(CrateSight.Sees(Observer(FarObserverAt, opticsLayers: 1), crate, field));   // 270 → 297
    }

    [Fact]
    public void A_wall_between_hides_a_crate_until_it_falls()
    {
        Battlefield field = TestWorld.FlatField(Piece(PropKind.BrickWall, 160, 1));
        Assert.False(Sees(field));
        field.SetDurability(1, 0);
        Assert.False(field.IsStanding(1));
        Assert.True(Sees(field));
    }

    [Theory]
    [InlineData(PropKind.StoneWall)]
    [InlineData(PropKind.House)]
    public void A_stone_wall_or_a_house_between_hides_a_crate(PropKind kind)
    {
        Prop prop = kind == PropKind.House
            ? new Prop(1, PropKind.House, new Obb2(new Vector2(100, 160), new Vector2(5, 4), 0), -0.5f, 6f)
            : Piece(kind, 160, 1);
        Assert.False(Sees(TestWorld.FlatField(prop)));
    }

    [Fact]
    public void A_rock_between_hides_a_crate() => Assert.False(Sees(TestWorld.FlatField(Slab(1, 160, 6f))));

    [Fact]
    public void A_rise_in_the_ground_between_hides_a_crate()
    {
        Assert.False(Sees(TestWorld.RidgeField(160f, 5f)));
        Assert.True(Sees(TestWorld.RidgeField(160f, 1f)));   // under the sight line
    }

    [Fact]
    public void Looking_without_a_field_throws() =>
        Assert.Throws<ArgumentNullException>(() => CrateSight.Sees(Observer(ObserverAt), CrateAt, null!));

    [Fact]
    public void Sight_is_tested_against_the_top_of_the_sign()
    {
        // 2 m short of the crate the line from the turret top (2.8 m) to the sign's top (1.8 m) runs at about 1.83 m:
        // a 1.2 m rock there hides the box but not the sign, a 2 m one hides both.
        Assert.True(Sees(TestWorld.FlatField(Slab(1, CrateAt.Y + 2f, 1.2f))));
        Assert.False(Sees(TestWorld.FlatField(Slab(1, CrateAt.Y + 2f, 2f))));
        Assert.Equal(1.8f, CrateRules.SignTop);
    }

    [Fact]
    public void The_sign_stands_on_the_ground_under_the_crate()
    {
        // Ground rising to the north at 1 in 10: the crate's ground is 6 m above the observer's; the sign's top is 1.8 m
        // above that, and the line to it clears the slope. A sign taken at height 0 would be under the ground.
        Battlefield field = TestWorld.Field(TestWorld.RampNorth(0.1f));
        Assert.Equal(7f, field.Terrain.HeightAt(CrateAt.X, CrateAt.Y), 1e-3f);
        Assert.True(Sees(field));
    }

    [Fact]
    public void A_wreck_sees_nothing()
    {
        TankState wreck = Observer(ObserverAt);
        wreck.IsDestroyed = true;
        Assert.False(CrateSight.Sees(wreck, CrateAt, TestWorld.FlatField()));
    }

    [Fact]
    public void Fences_bushes_and_trees_on_the_line_do_not_hide_a_crate()
    {
        Battlefield field = TestWorld.FlatField(
            Piece(PropKind.Fence, 170, 1),
            Bush(2, new Vector2(100, 160)),
            TreeTests.Tree(3, new Vector2(100, 150)),                // the trunk itself on the line
            TreeTests.Tree(4, CrateAt + new Vector2(0, 2f)));        // the crate under its crown
        Assert.True(Sees(field));
    }
}
