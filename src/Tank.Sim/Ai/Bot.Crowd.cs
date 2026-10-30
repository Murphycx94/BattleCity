using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Navigation;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>
/// Crowds and getting stuck in a royale (batch C review follow-up; stage 6A plan, deviation 27): fifteen bots in a ring
/// of a few tens of metres, every one of them another's enemy, so the team-mate rules (Bot.Mates.cs) never applied.
/// There a bot steers round every tank as round a team-mate (<see cref="AroundMates"/>), a holding or waiting bot makes
/// room for one pressing on it or backing into it (<see cref="MakeRoom"/>), and a bot that got stuck turning backs off
/// turning the other way (<see cref="ReverseSteer"/>). Off the grid's open ground (on a rock's or a cliff foot's closed
/// cells, where backing off leaves a tank) its searches start on its own level (<see cref="SearchStart"/>), and when one
/// finds no way it drives for open ground first (<see cref="LeaveClosedGround"/>). It never drives over a cliff's edge
/// (<see cref="DropAhead"/>; the cliff-foot traps fix). The other modes are unchanged.
/// </summary>
public sealed partial class Bot
{
    /// <summary>A bot pressing on a holding one is one whose circle (<see cref="NavDisc.RadiusOf"/>) comes within this of its own, m.</summary>
    private const float RoomGap = 1f;

    /// <summary>A bot cut off from the grid looks for open ground this many cells each way (4 m cells).</summary>
    private const int OpenGroundCells = 4;

    /// <summary>A search from a closed cell starts from an open cell whose ground is no more than this above or below the tank's, m.</summary>
    private const float StartRise = 2f;

    /// <summary>Looking along its way for a drop (<see cref="DropAhead"/>), a bot looks this far apart, m.</summary>
    private const float DropLook = 1f;

    /// <summary>A bot braking for a drop ahead turns to the side with none this far off its heading (<see cref="AwayFromDrop"/>), radians.</summary>
    private static readonly float DropTurn = Angles.Deg(30);

    private sbyte _lastSteer;          // the steer of the last command it drove with (Move): a stuck backs off turning the other way

    /// <summary>Whether <paramref name="t"/> is one to steer round as a team-mate: one, or in a royale any other tank.</summary>
    private bool KeepsOff(in TankState me, in TankState t) =>
        t.Id != TankId && !t.IsDestroyed && (t.Team == me.Team || _director.ZoneOn);

    /// <summary>
    /// Holding, or waiting for a path, in a royale: whether a bot trying to get somewhere (driving, waiting for a path or
    /// backing off) presses on it, its circle within <see cref="RoomGap"/> of this one's (<see cref="Presses"/>); then
    /// <paramref name="throttle"/> rolls this one along its hull, the way that takes it further from every tank that
    /// close (the pressing ones counting double; on, when they are square beside it). It holds again wherever it stops.
    /// </summary>
    private bool MakeRoom(in TankState me, out sbyte throttle)
    {
        throttle = 0;
        Vector2 forward = Angles.Forward(me.Yaw);
        float mine = NavDisc.RadiusOf(me.Spec), push = 0f;
        bool pressed = false;
        ReadOnlySpan<TankState> tanks = _director.Sim.Tanks;
        for (int i = 0; i < tanks.Length; i++)
        {
            ref readonly TankState t = ref tanks[i];
            if (t.Id == TankId || t.IsDestroyed) continue;
            Vector2 from = me.Position - t.Position;
            float d = from.Length(), gap = d - mine - NavDisc.RadiusOf(t.Spec);
            if (gap >= RoomGap || d < 1e-4f) continue;
            float weight = RoomGap - gap;
            if (Presses(t, from))
            {
                pressed = true;
                weight *= 2f;
            }
            push += Vector2.Dot(from / d, forward) * weight;
        }
        if (!pressed) return false;
        throttle = (sbyte)(push >= 0f ? 1 : -1);
        return true;
    }

    /// <summary>
    /// Whether tank <paramref name="t"/>, <paramref name="toMe"/> from this one, is a bot trying to get to a goal beyond
    /// this one, or backing off (stuck) with its tail towards this one (boxed in between a rock it was stuck on and a bot
    /// holding behind it, a bot got nowhere for 46 s in a royale's last ring).
    /// </summary>
    private bool Presses(in TankState t, Vector2 toMe)
    {
        Bot? other = _director.Find(t.Id);
        if (other is not { Activity: BotActivity.Driving or BotActivity.Waiting or BotActivity.Reversing }) return false;
        return Vector2.Dot(other.Goal - t.Position, toMe) > 0f ||
               other.Activity == BotActivity.Reversing && Vector2.Dot(Angles.Forward(t.Yaw), toMe) < 0f;
    }

    /// <summary>
    /// Whether driving on with <paramref name="throttle"/>, heading <paramref name="yaw"/>, takes the hull over a drop it
    /// could not stop short of: from ground it may climb, ground too steep to climb (<see cref="TankMotion.IsOnSteepGround"/>)
    /// and lower than its own lies along its way within half its length beyond its braking distance. No path leads down
    /// a cliff (the grid has its cells closed), but steering round a tank, or cutting a corner, takes a bot over the edge
    /// of the plateau or a ramp, into a pocket among the rocks at the cliff's foot it can't leave by any path (a light
    /// tank steering round another at full speed up the south ramp went over its west edge into the one below it).
    /// </summary>
    private bool DropAhead(in TankState me, sbyte throttle, float yaw)
    {
        if (throttle == 0) return false;
        HeightField terrain = _director.Sim.Field.Terrain;
        if (TankMotion.IsOnSteepGround(me.Spec, terrain, me.Position)) return false;
        Vector2 way = Angles.Forward(yaw) * throttle;
        float here = terrain.HeightAt(me.Position.X, me.Position.Y);
        float reach = me.Spec.HullLength / 2 + me.Speed * me.Speed / (2 * me.Spec.Braking);
        for (float d = DropLook; d <= reach + DropLook / 2; d += DropLook)
        {
            Vector2 p = me.Position + way * d;
            if (TankMotion.IsOnSteepGround(me.Spec, terrain, p) && terrain.HeightAt(p.X, p.Y) < here) return true;
        }
        return false;
    }

    /// <summary>
    /// A drop ahead (<see cref="DropAhead"/>), braking: the steer that turns the hull to the side with no drop within
    /// <see cref="DropTurn"/> of its heading, the way it was steering if that side has none; straight on if both have one.
    /// </summary>
    private sbyte AwayFromDrop(in TankState me, sbyte steer)
    {
        sbyte first = steer != 0 ? steer : (sbyte)1;   // +yaw is to the left; Steer +1 turns right
        if (!DropAhead(me, 1, me.Yaw - first * DropTurn)) return first;
        if (!DropAhead(me, 1, me.Yaw + first * DropTurn)) return (sbyte)-first;
        return 0;
    }

    /// <summary>
    /// A search found no path in a royale: when the tank isn't on open ground (<see cref="OnOpenGround"/>: backing off
    /// after a stuck can put it on the closed cells of a rock or a slope, and a search from there snaps to the nearest
    /// open cell, which may be a pocket closed all round), it drives straight for the nearest open ground
    /// (<see cref="OpenGroundNear"/>), then asks again from there, rather than asking from where it is, never moving,
    /// one search after another. Whether it does.
    /// </summary>
    private bool LeaveClosedGround(in TankState me, int tick)
    {
        if (OnOpenGround(me.Position) || !OpenGroundNear(me.Position, out Vector2 to) || Vector2.Distance(me.Position, to) <= PassRadius) return false;
        _path[0] = to;
        (_pathCount, _pathIndex, _pathCut, _pathGoal, _pathFrom) = (1, 0, true, Goal, me.Position);
        (_needPath, _askAfter, _replanAt) = (false, 0, tick + BotDirector.ReplanTicks);
        return true;
    }

    /// <summary>Whether the cell under <paramref name="p"/> is open with every cell round it open.</summary>
    private bool OnOpenGround(Vector2 p)
    {
        NavGrid grid = _director.Grid;
        (int i, int j) = NavGrid.CellOf(p);
        return grid.IsOpen(i, j) && OpenRound(grid, i, j) == 8;
    }

    /// <summary>
    /// The centre of the cell nearest <paramref name="p"/> within <see cref="OpenGroundCells"/> cells each way that is
    /// open with every cell round it open (ties: lower j, then lower i): ground that is no pocket.
    /// </summary>
    private bool OpenGroundNear(Vector2 p, out Vector2 centre)
    {
        NavGrid grid = _director.Grid;
        (int ci, int cj) = NavGrid.CellOf(p);
        float best = float.MaxValue;
        centre = default;
        for (int j = cj - OpenGroundCells; j <= cj + OpenGroundCells; j++)
        for (int i = ci - OpenGroundCells; i <= ci + OpenGroundCells; i++)
        {
            if (!grid.IsOpen(i, j) || OpenRound(grid, i, j) < 8) continue;
            Vector2 c = NavGrid.CentreOf(i, j);
            float d = Vector2.DistanceSquared(c, p);
            if (d >= best) continue;
            (best, centre) = (d, c);
        }
        return best < float.MaxValue;
    }

    /// <summary>
    /// Where a royale bot's search starts from: where it is, or, standing on a closed cell, the centre of the nearest open
    /// cell within <see cref="PathFinder.SnapRadius"/> whose ground is within <see cref="StartRise"/> of its own (ties:
    /// lower j, then lower i). The search would snap to the nearest open cell whatever its height, and at the foot of a
    /// cliff that is often on top of it: a path the tank can't start on, driving into the cliff face for good.
    /// </summary>
    private Vector2 SearchStart(Vector2 p)
    {
        NavGrid grid = _director.Grid;
        (int ci, int cj) = NavGrid.CellOf(p);
        if (grid.IsOpen(ci, cj)) return p;
        HeightField terrain = _director.Sim.Field.Terrain;
        float here = terrain.HeightAt(p.X, p.Y), best = PathFinder.SnapRadius * PathFinder.SnapRadius;
        int reach = (int)MathF.Ceiling(PathFinder.SnapRadius / NavGrid.CellSize);
        Vector2 start = p;
        for (int j = cj - reach; j <= cj + reach; j++)
        for (int i = ci - reach; i <= ci + reach; i++)
        {
            if (!grid.IsOpen(i, j)) continue;
            Vector2 c = NavGrid.CentreOf(i, j);
            float d = Vector2.DistanceSquared(c, p);
            if (d >= best || MathF.Abs(terrain.HeightAt(c.X, c.Y) - here) > StartRise) continue;
            (best, start) = (d, c);
        }
        return start;
    }

    /// <summary>How many of the eight cells round (<paramref name="i"/>, <paramref name="j"/>) are open.</summary>
    private static int OpenRound(NavGrid grid, int i, int j)
    {
        int n = 0;
        for (int dj = -1; dj <= 1; dj++)
        for (int di = -1; di <= 1; di++)
            if ((di != 0 || dj != 0) && grid.IsOpen(i + di, j + dj)) n++;
        return n;
    }

    /// <summary>
    /// The way the hull turns backing off after a stuck: in a royale, stuck while turning, against that turn (it is what
    /// something blocks: a rock at a corner, a tank alongside), unless the last back-off already turned that way (it got
    /// it nowhere: stuck the same way again), then the other way; stuck going straight, the other way from the last
    /// back-off. Elsewhere one way and the next time the other.
    /// </summary>
    private sbyte ReverseSteer()
    {
        if (!_director.ZoneOn) return (sbyte)(Stucks % 2 == 1 ? 1 : -1);
        sbyte against = (sbyte)-_lastSteer;
        return against != 0 && _reverseSteer != against ? against : (sbyte)(_reverseSteer > 0 ? -1 : 1);
    }
}
