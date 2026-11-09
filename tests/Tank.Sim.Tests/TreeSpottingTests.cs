using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>
/// Tree crowns hide tanks like bushes (riverland design §2.4, stage 3 design §2.2): a medium tank at (100, 190) looks
/// north (−Z) at a still light tank 120 m away, side on, on flat ground. The trees' trunks are 0.5 m, their crowns 3.5 m.
/// </summary>
public sealed class TreeSpottingTests
{
    private static readonly Vector2 ObserverAt = new(100, 190);
    private static readonly Vector2 TargetAt = ObserverAt - new Vector2(0, 120);
    private const int Tick = 1000;

    private static TankState Observer() => new() { Id = 1, Spec = TankCatalog.Medium, Position = ObserverAt, LastFired = TankState.NeverFired };

    private static TankState Target() => new()
    {
        Id = 2, Spec = TankCatalog.Light, Team = 1, Position = TargetAt, Yaw = MathF.PI / 2, LastFired = TankState.NeverFired,
    };

    private static Prop Tree(int id, Vector2 at) => TreeTests.Tree(id, at);

    private static Prop Bush(int id, Vector2 at, float radius = 3f) =>
        new(id, PropKind.Bush, new Obb2(at, new Vector2(radius, radius), 0), -0.2f, 4.5f);

    private static Sighting Look(params Prop[] props) => Look(TestWorld.FlatField(props));

    private static Sighting Look(Battlefield field) => Spotting.Look(Observer(), Target(), field, Tick, targetMoving: false);

    [Fact]
    public void A_crown_near_the_target_hides_like_a_bush()
    {
        Sighting open = Look();
        Sighting hidden = Look(Tree(1, TargetAt + new Vector2(0, 8f)));   // on the line, 8 m short of the target
        Assert.Equal(0, open.Bushes);
        Assert.Equal(1, hidden.Bushes);
        Assert.Equal(open.Camo + Spotting.BushBonus, hidden.Camo, 1e-5f);
    }

    [Fact]
    public void Trees_and_bushes_count_together_up_to_two()
    {
        Sighting look = Look(Tree(1, TargetAt + new Vector2(0, 5f)), Tree(2, TargetAt + new Vector2(0, 10f)), Bush(3, TargetAt));
        Assert.Equal(2, look.Bushes);
    }

    [Theory]
    [InlineData(2.5f, 1)]   // within 0.75 × 3.5 = 2.625 m of the trunk: under the tree
    [InlineData(3.0f, 0)]   // beside it, the sight line not through the crown's inner part
    public void Under_a_tree_counts_within_three_quarters_of_the_crown(float east, int bushes) =>
        Assert.Equal(bushes, Look(Tree(1, TargetAt + new Vector2(east, 0))).Bushes);

    [Fact]
    public void A_felled_tree_hides_nothing()
    {
        Battlefield field = TestWorld.FlatField(Tree(1, TargetAt + new Vector2(0, 8f)));
        Assert.Equal(1, Look(field).Bushes);
        field.Destroy(0);
        Assert.Equal(0, Look(field).Bushes);
    }

    [Fact]
    public void A_crown_far_from_the_target_does_not_count() =>
        Assert.Equal(0, Look(Tree(1, TargetAt + new Vector2(0, 30f))).Bushes);

    [Fact]
    public void Sees_agrees_with_Look_among_trees()
    {
        // 200 trees (trunks 0.35–0.6 m, 9–15 m tall) in a 60 m grove round (100, 100), a few bushes among them; observers
        // and targets anywhere on the field, still or moving, some just after a shot.
        var rng = new XorShift(3);
        var props = new List<Prop>();
        for (int k = 0; k < 200; k++)
            props.Add(TreeTests.Tree(props.Count + 1, new Vector2(Between(ref rng, 70, 130), Between(ref rng, 70, 130)),
                Between(ref rng, 0.35f, 0.6f), Between(ref rng, 9f, 15f)));
        for (int k = 0; k < 10; k++)
            props.Add(Bush(props.Count + 1, new Vector2(Between(ref rng, 70, 130), Between(ref rng, 70, 130)), Between(ref rng, 2f, 4f)));
        Battlefield field = TestWorld.FlatField(props.ToArray());
        TankSpec[] specs = TankCatalog.All.ToArray();
        int seen = 0, unseen = 0, hidden = 0;
        for (int k = 0; k < 500; k++)
        {
            var observer = new TankState { Id = 1, Spec = specs[rng.NextUInt() % (uint)specs.Length], Position = Anywhere(ref rng), Yaw = Between(ref rng, 0, Angles.TwoPi) };
            var target = new TankState
            {
                Id = 2, Team = 1, Spec = specs[rng.NextUInt() % (uint)specs.Length], Position = Anywhere(ref rng), Yaw = Between(ref rng, 0, Angles.TwoPi),
                LastFired = rng.NextUInt() % 4 == 0 ? Tick - 10 : TankState.NeverFired,
            };
            bool moving = rng.NextUInt() % 2 == 0;
            Sighting look = Spotting.Look(observer, target, field, Tick, moving);
            Assert.True(look.Seen == Spotting.Sees(observer, target, field, Tick, moving), $"pair {k}: {observer.Position} → {target.Position}");
            if (look.Seen) seen++; else unseen++;
            if (look.Bushes > 0) hidden++;
        }
        Assert.True(seen > 100 && unseen > 40 && hidden > 100, $"{seen} seen, {unseen} unseen, {hidden} hidden by foliage");
    }

    /// <summary>Anywhere on the playable field, the grove more often than not.</summary>
    private static Vector2 Anywhere(ref XorShift rng) =>
        rng.NextUInt() % 3 == 0
            ? new Vector2(Between(ref rng, 10, TestWorld.Size - 10), Between(ref rng, 10, TestWorld.Size - 10))
            : new Vector2(Between(ref rng, 60, 140), Between(ref rng, 60, 140));

    private static float Between(ref XorShift rng, float min, float max) => min + rng.NextFloat() * (max - min);
}
