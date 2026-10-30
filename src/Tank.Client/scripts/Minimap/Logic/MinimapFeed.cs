using Tank.Client.Match;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Client.Minimap;

/// <summary>Fills a <see cref="MinimapFrame"/> from a match's data (stage 5 design §5). Allocates nothing.</summary>
public static class MinimapFeed
{
    /// <summary>TankCatalog.All's index (an entity's Spec) as a shape; an unknown index draws as Medium.</summary>
    /// <remarks>The catalog's order is guarded by MinimapFeedTests.Shapes_follow_the_catalog_s_order_and_an_unknown_index_is_medium.</remarks>
    public static MinimapShape ShapeOf(int specIndex) => specIndex switch
    {
        0 => MinimapShape.Light,
        2 => MinimapShape.Heavy,
        3 => MinimapShape.Td,
        _ => MinimapShape.Medium,
    };

    /// <summary><paramref name="spec"/>'s shape by its place in <c>TankCatalog.All</c>; a spec not in the catalog draws as Medium.</summary>
    public static MinimapShape ShapeOf(TankSpec spec)
    {
        IReadOnlyList<TankSpec> all = TankCatalog.All;
        for (int i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], spec)) return ShapeOf(i);
        return MinimapShape.Medium;
    }

    /// <summary>
    /// Every tank in <paramref name="others"/> — the snapshot's, which the server has already filtered to what the
    /// player's team may see (net design §5.5) — and nothing else: no scoreboard row, no memory of earlier frames. Sides
    /// by <see cref="MatchColors.SideOf"/>; the tank with id <paramref name="eyeTankId"/> (the one followed, which
    /// carries the arrow) is left out; 0 leaves none out.
    /// </summary>
    public static void AddOthers(MinimapFrame frame, IReadOnlyList<RemoteTank> others, BattleMode mode, byte ownTeam, int eyeTankId)
    {
        for (int i = 0; i < others.Count; i++)   // indexed: an interface foreach would box the list's enumerator
        {
            RemoteTank other = others[i];
            BattleEntity e = other.Entity;
            if (eyeTankId != 0 && e.Id == eyeTankId) continue;
            frame.AddTank(new MinimapTank(
                other.Position,
                MatchColors.SideOf(mode, ownTeam, e.Team, isOwn: false),
                ShapeOf(e.Spec),
                e.Has(TankFlags.Destroyed)));
        }
    }

    /// <summary>A standard match's two bases (none when <paramref name="bases"/> is null or empty), points from <paramref name="match"/>.</summary>
    public static void AddBases(MinimapFrame frame, IReadOnlyList<TeamBase>? bases, byte ownTeam, in MatchBlock match)
    {
        if (bases is null) return;
        for (int i = 0; i < bases.Count; i++)
        {
            TeamBase b = bases[i];
            // BaseProgressN is how far team N's base has been taken.
            byte points = b.Team == 0 ? match.BaseProgress0 : match.BaseProgress1;
            frame.AddBase(new MinimapBase(b.Centre, b.Radius, b.Team == ownTeam, points));
        }
    }

    /// <summary>
    /// The zone of <paramref name="state"/> (the client's <c>ZoneAt</c> for the frame): nothing for Phase None; the safe circle
    /// now, with the next ring while waiting or shrinking (when known), none once closed.
    /// </summary>
    public static void AddZone(MinimapFrame frame, in ZoneState state)
    {
        if (state.Phase == ZonePhase.None) return;
        // HasNext alone decides: ZoneState.At gives no Next once closed (the last ring is all there is), nor while a ring is still unannounced.
        frame.Zone = new MinimapZone(state.Current.Centre, state.Current.Radius, state.HasNext, state.Next.Centre, state.Next.Radius);
    }
}
