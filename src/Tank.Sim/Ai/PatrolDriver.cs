using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>A point on a patrol route, and how long to stand there (0: no wait).</summary>
public readonly record struct PatrolPoint(Vector2 Position, float WaitSeconds);

/// <summary>
/// Drives one tank round a looped route (stage 3 design §4.2), producing its <see cref="InputCmd"/> each tick: it heads
/// for the next point (turning on the spot first when that lies more than <see cref="TurnInPlace"/> off its heading),
/// brakes in time to stop on a point it waits at or where the route turns sharper than that, and while waiting fires AP
/// every <see cref="FireEvery"/> s at the ground <see cref="AimShort"/> m short of a given point (the player) and
/// <see cref="AimAside"/> m to its left, so that it gives itself away without hitting anything there; the first shot no
/// sooner than <see cref="FirstShot"/> s after it stops, only once the gun is laid, and none at all while the point is
/// within <see cref="HoldFireWithin"/> m (nor when the gun can't be laid on the aim, beyond its elevation or
/// depression). While driving the gun looks straight ahead. A wreck holds still; once revived it drives on from where it
/// stands to the point it was heading for (a wait that ran out while it lay wrecked is over). Its state is its own, not
/// the simulation's, and it leaves the ammunition to the host: once the AP racks are empty it fires no more.
/// <para>
/// What a route has to allow for: braking looks only at the point being driven to, so a stop needs a run-up of its
/// braking distance (about 15 m at a light tank's top speed) after the point before it; and a stop the hull can't get
/// within <see cref="StopRadius"/> of (a tank parked on it) holds the patrol there, pushing, until the way is clear.
/// </para>
/// </summary>
public sealed class PatrolDriver
{
    /// <summary>A point driven through counts as reached this close, m.</summary>
    public const float PassRadius = 3f;

    /// <summary>A point to wait at, or a sharp corner, counts as reached once the hull has stopped within this of it, m.</summary>
    public const float StopRadius = Steering.StopRadius;

    /// <summary>
    /// A point further off the heading than this (radians) is turned to on the spot (<see cref="Steering"/>); a route
    /// point where the route turns more than this is stopped at, like a point waited at.
    /// </summary>
    public static readonly float TurnInPlace = Steering.TurnInPlace;

    /// <summary>Seconds between shots while waiting.</summary>
    public const float FireEvery = 4f;

    /// <summary>Seconds after stopping before the first shot.</summary>
    public const float FirstShot = 2.5f;

    /// <summary>The aim is this far short of the point fired at, m.</summary>
    public const float AimShort = 25f;

    /// <summary>
    /// The aim is also this far to the side of the line to the point, m: the shot comes in low, so a little dispersion
    /// upwards carries it well past the aim, and this way it still flies by the point at least this far off.
    /// </summary>
    public const float AimAside = 8f;

    /// <summary>Nearer than this the point gets no shot at all, m.</summary>
    public const float HoldFireWithin = 2 * AimShort;

    /// <summary>The gun fires only once it is laid this close to the aim, in turret yaw and gun pitch (radians).</summary>
    public static readonly float OnTarget = Steering.OnTarget;

    private readonly PatrolPoint[] _route;
    private readonly bool[] _sharp;   // per point: the route turns more than TurnInPlace there
    private int _waitUntil = -1;
    private int _nextShot;

    public PatrolDriver(IReadOnlyList<PatrolPoint> route)
    {
        if (route.Count < 2) throw new ArgumentException("A patrol needs at least two points.", nameof(route));
        _route = route.ToArray();
        _sharp = new bool[route.Count];
        for (int i = 0; i < route.Count; i++)
        {
            Vector2 before = route[(i + route.Count - 1) % route.Count].Position, at = route[i].Position, after = route[(i + 1) % route.Count].Position;
            _sharp[i] = MathF.Abs(Angles.Wrap(Angles.YawOf(after - at) - Angles.YawOf(at - before))) > TurnInPlace;
        }
    }

    /// <summary>The index of the point being driven to, or waited at.</summary>
    public int Next { get; private set; }

    /// <summary>Standing at a point it waits at (and shooting).</summary>
    public bool Waiting => _waitUntil >= 0;

    /// <summary>The tank's command for <paramref name="tick"/>; <paramref name="shootAt"/> is the point it fires short of.</summary>
    public InputCmd Command(in TankState tank, Battlefield field, int tick, Vector2 shootAt)
    {
        var hold = new InputCmd(tick, 0, 0, tank.Yaw, 0f, false);
        if (tank.IsDestroyed) return hold;
        if (Waiting)
        {
            if (tick < _waitUntil) return Shoot(tank, field, tick, shootAt);
            _waitUntil = -1;
            Next = (Next + 1) % _route.Length;
        }
        PatrolPoint point = _route[Next];
        float distance = Vector2.Distance(tank.Position, point.Position);
        bool stopsHere = point.WaitSeconds > 0 || _sharp[Next];
        if (stopsHere ? Steering.StoppedAt(tank, point.Position) : distance <= PassRadius)
        {
            if (point.WaitSeconds > 0)
            {
                _waitUntil = tick + (int)MathF.Round(point.WaitSeconds * SimConstants.TickRate);
                _nextShot = tick + (int)MathF.Round(FirstShot * SimConstants.TickRate);
                return hold;
            }
            Next = (Next + 1) % _route.Length;
            point = _route[Next];
            distance = Vector2.Distance(tank.Position, point.Position);
            stopsHere = point.WaitSeconds > 0 || _sharp[Next];
        }
        var (throttle, steer) = Steering.Toward(tank, point.Position, stopsHere);
        return hold with { Throttle = throttle, Steer = steer };
    }

    /// <summary>
    /// Lays the gun on the ground <see cref="AimShort"/> m short of <paramref name="shootAt"/> and <see cref="AimAside"/>
    /// m to its left (seen from the tank) and, when a shot is due and the gun has come round to within
    /// <see cref="OnTarget"/>, fires. Within <see cref="HoldFireWithin"/> of the point the gun looks straight ahead instead.
    /// </summary>
    private InputCmd Shoot(in TankState tank, Battlefield field, int tick, Vector2 shootAt)
    {
        Vector2 toward = shootAt - tank.Position;
        float length = toward.Length();
        if (length <= HoldFireWithin) return new InputCmd(tick, 0, 0, tank.Yaw, 0f, false);
        Vector2 along = toward / length;
        Vector2 ground = shootAt - along * AimShort + new Vector2(along.Y, -along.X) * AimAside;   // (y, −x): to the left
        var aim = new Vector3(ground.X, field.Terrain.HeightAt(ground.X, ground.Y), ground.Y);
        TankPose pose = TankPose.Compute(tank, field.Terrain);
        var (yaw, pitch) = Angles.AimAngles(aim - pose.Trunnion(tank.Spec), tank.Yaw);
        var (turret, gun) = pose.ToHullAngles(Angles.Direction(yaw, pitch), tank.TurretYaw);
        bool fire = tick >= _nextShot && Steering.IsLaid(tank, turret, gun);
        if (fire) _nextShot = tick + (int)MathF.Round(FireEvery * SimConstants.TickRate);
        return new InputCmd(tick, 0, 0, yaw, pitch, fire, AmmoType.AP);
    }
}
