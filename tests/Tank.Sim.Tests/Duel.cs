using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// Two medium tanks on flat ground: the shooter in slot 0 at (100, 150) facing north, the target in slot 1 at (100, 100)
/// turned to <c>targetYaw</c> (π shows the shooter its front). The target's input holds its turret straight ahead.
/// </summary>
internal sealed class Duel
{
    public static readonly TankSpec Spec = TankCatalog.Medium;
    public static readonly Vector2 ShooterAt = new(100, 150);
    public static readonly Vector2 TargetAt = new(100, 100);

    private readonly InputCmd[] _inputs = new InputCmd[2];

    public Duel(float targetYaw, params Prop[] props)
    {
        Sim = new Simulation(TestWorld.FlatField(props));
        ShooterId = Sim.AddTank(Spec, ShooterAt, 0f);
        TargetId = Sim.AddTank(Spec, TargetAt, targetYaw);
        TargetYaw = targetYaw;
    }

    public Simulation Sim { get; }
    public int ShooterId { get; }
    public int TargetId { get; }
    public float TargetYaw { get; }
    public TankState Shooter => Sim.Tanks[0];
    public TankState Target => Sim.Tanks[1];

    /// <summary>A point on the target: <paramref name="local"/> in hull space (x right, y up, −z forward), turned by its yaw.</summary>
    public Vector3 OnTarget(Vector3 local) =>
        new Vector3(TargetAt.X, 0, TargetAt.Y) + Vector3.Transform(local, Quaternion.CreateFromAxisAngle(Vector3.UnitY, TargetYaw));

    /// <summary>Lays the gun on <paramref name="point"/> and lets turret, gun and aim circle settle for two seconds.</summary>
    public InputCmd AimAt(Vector3 point, AmmoType ammo = AmmoType.AP)
    {
        TankPose pose = TankPose.Compute(Shooter, Sim.Field.Terrain);
        var (yaw, pitch) = Angles.AimAngles(point - pose.Trunnion(Spec));
        var aim = new InputCmd(0, 0, 0, yaw, pitch, false, ammo);
        Run(aim, 60);
        return aim;
    }

    /// <summary>Fires once at the current aim and follows the shell for half a second.</summary>
    public List<GameEvent> Fire(InputCmd aim)
    {
        var events = Run(aim with { Fire = true }, 1);
        events.AddRange(Run(aim, 15));
        return events;
    }

    public List<GameEvent> Run(InputCmd shooter, int ticks, InputCmd? target = null)
    {
        var events = new List<GameEvent>();
        for (int i = 0; i < ticks; i++)
        {
            _inputs[0] = shooter;
            _inputs[1] = target ?? new InputCmd(0, 0, 0, TargetYaw, 0, false);
            Sim.Step(_inputs);
            events.AddRange(Sim.Events);
        }
        return events;
    }
}
