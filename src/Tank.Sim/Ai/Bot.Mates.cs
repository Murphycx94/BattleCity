using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Navigation;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>
/// Keeping off team-mates (5C batch C review follow-up; see <see cref="Bot"/>'s remarks): a team-mate's tank shoved by a
/// bot is a correction its player sees, and the bots' paths only steer round tanks that have stood still.
/// </summary>
public sealed partial class Bot
{
    /// <summary>A driving bot's way is bent at most this far off the way to its path point, radians.</summary>
    private static readonly float MateMaxBend = Angles.Deg(60);

    /// <summary>How hard a team-mate bends the way: at full weight, this far sideways for every metre on.</summary>
    private const float MateBendGain = 2f;

    /// <summary>Nearer head-on than this (sideways share of the way apart) a team-mate is passed keeping right.</summary>
    private const float HeadOn = 0.2f;

    /// <summary>A team-mate heading within 30° of straight at the bot is coming at it (cos 30°): the bot doesn't yield to it, it gets out of its way.</summary>
    private const float ComingAt = 0.866f;

    private int _shiftUntil = -1;      // stopping to fire: rolling clear of team-mates up to (not including) this tick

    /// <summary>
    /// <paramref name="point"/> as a driving bot steers for it among its team-mates. Each live team-mate whose circle
    /// (<see cref="NavDisc.RadiusOf"/>) would come within <see cref="BotDirector.MateGap"/> of the bot's own in the next
    /// <see cref="BotDirector.MateLookAhead"/> seconds, both going on as they are, and that is or will be ahead of it,
    /// bends the way away from where it would be then: the more the nearer that is (full at circles overlapping by
    /// <see cref="BotDirector.MateGap"/>) and the sooner; one met nearly head-on is passed keeping right (both bots keep
    /// right, so they part). The bend is at most <see cref="MateMaxBend"/>; the point steered for is as far off as
    /// <paramref name="point"/>, so braking to stop on it is unchanged. <paramref name="yield"/>: a moving team-mate
    /// ahead of the hull, not coming straight at it, would overlap circles with it: it lets that one by, throttle off,
    /// still steering (one coming at it head-on it gets out of the way of instead). Slot order, no allocation.
    /// </summary>
    private Vector2 AroundMates(in TankState me, Vector2 point, out bool yield)
    {
        yield = false;
        Vector2 to = point - me.Position;
        float distance = to.Length();
        if (distance < 1e-3f) return point;
        Vector2 way = to / distance, right = new(-way.Y, way.X);
        Vector2 forward = Angles.Forward(me.Yaw), velocity = forward * me.Speed;
        float mine = NavDisc.RadiusOf(me.Spec), horizon = BotDirector.MateLookAhead;
        Vector2 bend = Vector2.Zero;
        ReadOnlySpan<TankState> tanks = _director.Sim.Tanks;
        for (int i = 0; i < tanks.Length; i++)
        {
            ref readonly TankState t = ref tanks[i];
            if (!KeepsOff(me, t)) continue;
            Vector2 its = Angles.Forward(t.Yaw) * t.Speed, rel = t.Position - me.Position, closing = its - velocity;
            float reach = mine + NavDisc.RadiusOf(t.Spec), far = reach + BotDirector.MateGap + horizon * closing.Length();
            if (rel.LengthSquared() > far * far) continue;
            float speed2 = closing.LengthSquared();
            float when = speed2 > 1e-6f ? System.Math.Clamp(-Vector2.Dot(rel, closing) / speed2, 0f, horizon) : 0f;
            Vector2 apart = rel + closing * when;   // the team-mate as seen from the bot when they are nearest
            float gap = apart.Length() - reach;
            if (gap >= BotDirector.MateGap) continue;
            if (Vector2.Dot(apart, way) <= 0f && Vector2.Dot(rel, way) <= 0f) continue;   // behind it and staying so
            float weight = System.Math.Clamp((BotDirector.MateGap - gap) / (2f * BotDirector.MateGap), 0f, 1f) * (1f - 0.5f * when / horizon);
            Vector2 away = -apart - way * Vector2.Dot(-apart, way);   // sideways away from it
            float sideways = away.Length();
            bend += (sideways > HeadOn * apart.Length() && sideways > 1e-4f ? away / sideways : right) * weight;
            yield |= gap < 0f && Vector2.Dot(rel, forward) > 0f && MathF.Abs(t.Speed) >= Steering.StoppedSpeed &&
                     Vector2.Dot(its, -rel) < ComingAt * MathF.Abs(t.Speed) * rel.Length();
        }
        if (bend == Vector2.Zero) return point;
        Vector2 bent = Vector2.Normalize(way + bend * MateBendGain);
        float off = MathF.Atan2(way.X * bent.Y - way.Y * bent.X, Vector2.Dot(way, bent));
        if (MathF.Abs(off) > MateMaxBend) bent = Turn(way, MathF.CopySign(MateMaxBend, off));
        return me.Position + bent * distance;
    }

    /// <summary>
    /// Stopping to fire at <paramref name="enemy"/> (<see cref="Engage"/>), but not next to a team-mate: when the bot
    /// starts to engage on its way somewhere (not holding: one standing at its post stays there) it has
    /// <see cref="BotDirector.ShiftTicks"/> to find a spot clear of them. While the spot it would
    /// stop on, braking now, lies with its circle within <see cref="BotDirector.MateStopGap"/> of a live team-mate's, it
    /// rolls on or back along its hull (the way that takes it further from them; on, when they are square beside it),
    /// the gun on the enemy, not firing. Once that spot is clear, or the time is up, it stops and fires and stays put
    /// for the rest of the engagement, whoever comes alongside: a still tank is one its team-mates' players can predict.
    /// </summary>
    private InputCmd EngageApart(in TankState me, in TankState enemy, HeightField terrain, int tick, bool inSight)
    {
        if (Activity != BotActivity.Engaging) _shiftUntil = Activity == BotActivity.Holding ? -1 : tick + BotDirector.ShiftTicks;
        if (tick < _shiftUntil && CrowdedStop(me, out bool on))
        {
            Lay lay = Gunnery.Aim(me, enemy, terrain, out _);
            Activity = BotActivity.Engaging;
            if (_checkAt >= 0) _checkAt++;   // the stuck watch waits, as while firing
            return new InputCmd(tick, (sbyte)(on ? 1 : -1), 0, lay.Yaw, lay.Pitch, false);
        }
        _shiftUntil = -1;
        return Engage(me, enemy, terrain, tick, inSight);
    }

    /// <summary>
    /// Whether the spot the bot would stop on, braking now, has a live team-mate's circle within
    /// <see cref="BotDirector.MateStopGap"/> of its own; <paramref name="on"/>: rolling on along the hull takes it further
    /// from them (the pushes away from each, the deeper the harder, summed along the hull) than rolling back.
    /// </summary>
    private bool CrowdedStop(in TankState me, out bool on)
    {
        Vector2 forward = Angles.Forward(me.Yaw);
        Vector2 stop = me.Position + forward * (me.Speed * MathF.Abs(me.Speed) / (2f * me.Spec.Braking));
        float mine = NavDisc.RadiusOf(me.Spec), push = 0f;
        bool crowded = false;
        ReadOnlySpan<TankState> tanks = _director.Sim.Tanks;
        for (int i = 0; i < tanks.Length; i++)
        {
            ref readonly TankState t = ref tanks[i];
            if (t.Id == TankId || t.Team != me.Team || t.IsDestroyed) continue;
            Vector2 from = stop - t.Position;
            float d = from.Length(), gap = d - mine - NavDisc.RadiusOf(t.Spec);
            if (gap >= BotDirector.MateStopGap) continue;
            crowded = true;
            if (d > 1e-4f) push += Vector2.Dot(from / d, forward) * (BotDirector.MateStopGap - gap);
        }
        on = push >= 0f;
        return crowded;
    }
}
