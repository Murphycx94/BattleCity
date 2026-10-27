using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Modes;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>
/// Backing away from an enemy too close to lay the gun on (5C stalemate fix 2; see <see cref="Bot"/>'s remarks).
/// </summary>
public sealed partial class Bot
{
    /// <summary>Backing away from an enemy too close, the hull is steered once off the way by more than this, radians.</summary>
    private static readonly float BackOffAlign = Angles.Deg(2);

    /// <summary>Getting nowhere backing off, the way turns by these in turn (and back to straight away), radians.</summary>
    private static readonly float[] BackOffStuckTurns = { 0f, Angles.Deg(60), Angles.Deg(-60) };

    /// <summary>Inside its circle, a bot backing off looks this far along its way …, m.</summary>
    private const float BackOffLook = 6f;

    /// <summary>… and keeps that point this far inside the edge, sliding round it once there, m.</summary>
    private const float BackOffMargin = 4f;

    /// <summary>Backing off forward or in reverse, it changes over once its way is this far round to the other end of the hull (cos 120°).</summary>
    private const float BackOffSwitch = -0.5f;

    /// <summary>The enemy the picker named too close to lay on (<see cref="TargetPicker.TooClose"/>), still in the battle and alive: its slot.</summary>
    private bool TooClose(Simulation sim, out int slot)
    {
        slot = Picker.TooClose == 0 ? -1 : sim.SlotOf(Picker.TooClose);
        return slot >= 0 && !sim.Tanks[slot].IsDestroyed;
    }

    /// <summary>
    /// Backing away from <paramref name="enemy"/>, too close to lay the gun on, along <see cref="BackOffWay"/>: forward
    /// when it starts with the hull's front the nearer end to that way, otherwise in reverse, changing over only once the
    /// way has come well round (<see cref="BackOffSwitch"/>), steering onto it meanwhile (on the spot while it is more
    /// than <see cref="Steering.TurnInPlace"/> off: a heavy backing away turns wide); the gun (a turret's) on the
    /// enemy, never firing (a casemate going forward aims straight ahead: its gun would turn the hull). The stuck watch
    /// runs: each time it gets nowhere the way turns (<see cref="BackOffStuckTurns"/>).
    /// </summary>
    private InputCmd BackOff(in TankState me, in TankState enemy, HeightField terrain, int tick)
    {
        Vector2 way = BackOffWay(me, enemy);
        float along = Vector2.Dot(Angles.Forward(me.Yaw), way);
        if (!_backedOff) _backOffForward = along > 0f;
        else if ((_backOffForward ? along : -along) < BackOffSwitch) _backOffForward = !_backOffForward;
        float off = Angles.Wrap(Angles.YawOf(_backOffForward ? way : -way) - me.Yaw);
        sbyte steer = (sbyte)(MathF.Abs(off) <= BackOffAlign ? 0 : off > 0 ? -1 : 1);   // +yaw is to the left; Steer +1 turns right
        Lay lay = Gunnery.Aim(me, enemy, terrain, out _);
        sbyte throttle = (sbyte)(MathF.Abs(off) > Steering.TurnInPlace ? 0 : _backOffForward ? 1 : -1);
        var cmd = new InputCmd(tick, throttle, steer, lay.Yaw, lay.Pitch, false);
        if (_backOffForward && !me.Spec.HasTurret) cmd = cmd with { AimYaw = me.Yaw, AimPitch = 0f };
        Activity = BotActivity.Reversing;
        _backedOff = true;
        if (IsStuck(me, tick))
        {
            Stucks++;
            _backOffStucks++;
        }
        return cmd;
    }

    /// <summary>
    /// Straight away from <paramref name="enemy"/>, turned by <see cref="BackOffStuckTurns"/> for the times backing off
    /// got nowhere. Inside the circle its final leg must end in (an attacker capturing, a bot called back home), it keeps
    /// the point <see cref="BackOffLook"/> along the way <see cref="BackOffMargin"/> inside the edge: the way's outward
    /// part is cut to the room left (made inward once past it) and the rest turned along the edge, so near the edge it
    /// slides round it, still getting further from an enemy nearer the centre.
    /// </summary>
    private Vector2 BackOffWay(in TankState me, in TankState enemy)
    {
        Vector2 from = me.Position - enemy.Position;
        Vector2 away = from.LengthSquared() > 1e-6f ? Vector2.Normalize(from) : -Angles.Forward(me.Yaw);
        away = Turn(away, BackOffStuckTurns[_backOffStucks % BackOffStuckTurns.Length]);
        TeamBase circle = EndCircle;
        if (circle.Radius <= 0f || OutsideItsCircle(me)) return away;
        Vector2 radial = me.Position - circle.Centre;
        float r = radial.Length();
        if (r < 1e-3f) return away;
        Vector2 n = radial / r;
        float outward = Vector2.Dot(away, n), room = (circle.Radius - BackOffMargin - r) / BackOffLook;
        if (outward <= room) return away;
        float keep = System.Math.Clamp(room, -0.5f, outward);
        Vector2 along = away - n * outward;
        along = along.LengthSquared() > 1e-6f ? Vector2.Normalize(along) : new Vector2(-n.Y, n.X);   // straight out: one way round
        return along * MathF.Sqrt(1f - keep * keep) + n * keep;
    }

    /// <summary><paramref name="v"/> turned by <paramref name="angle"/> radians (positive: one way, negative: the other).</summary>
    private static Vector2 Turn(Vector2 v, float angle)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }
}
