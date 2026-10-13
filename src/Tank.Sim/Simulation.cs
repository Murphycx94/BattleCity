using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim;

/// <summary>
/// Advances the battle by one fixed 30Hz tick. Order inside a tick: tank footprints are snapshotted, each tank
/// drives, aims and fires (in slot order) against that snapshot, then every shell flies and resolves its hits.
/// </summary>
public sealed class Simulation
{
    public const int MaxTanks = 32;
    public const int MaxShells = 256;

    private readonly TankState[] _tanks = new TankState[MaxTanks];
    private readonly Shell[] _shells = new Shell[MaxShells];
    private readonly Obb2[] _footprints = new Obb2[MaxTanks];
    private readonly Obb2[] _others = new Obb2[MaxTanks];
    private readonly List<GameEvent> _events = new(16);
    private int _nextId = 1;

    public Simulation(Battlefield field) => Field = field;

    public Battlefield Field { get; }
    public int Tick { get; private set; }
    public int TankCount { get; private set; }
    public int ShellCount { get; private set; }
    public ReadOnlySpan<TankState> Tanks => _tanks.AsSpan(0, TankCount);
    public ReadOnlySpan<Shell> Shells => _shells.AsSpan(0, ShellCount);

    /// <summary>Events produced by the most recent <see cref="Step"/>.</summary>
    public IReadOnlyList<GameEvent> Events => _events;

    /// <returns>The new tank's id. It occupies the next slot in <see cref="Tanks"/>.</returns>
    public int AddTank(TankSpec spec, Vector2 position, float yaw)
    {
        if (TankCount == MaxTanks) throw new InvalidOperationException($"At most {MaxTanks} tanks.");
        int id = _nextId++;
        _tanks[TankCount++] = new TankState { Id = id, Spec = spec, Position = Field.ClampToPlayable(position), Yaw = Angles.Wrap(yaw) };
        return id;
    }

    /// <param name="inputs">One command per tank, in <see cref="Tanks"/> order.</param>
    public void Step(ReadOnlySpan<InputCmd> inputs)
    {
        if (inputs.Length != TankCount)
            throw new ArgumentException($"Expected {TankCount} inputs, got {inputs.Length}.", nameof(inputs));
        _events.Clear();
        Tick++;
        SnapshotFootprints();
        for (int i = 0; i < TankCount; i++)
        {
            ref TankState tank = ref _tanks[i];
            InputCmd input = Sanitize(inputs[i], tank);
            TankMotion.Drive(ref tank, input, Field, FootprintsExcept(i));
            TankMotion.AimGun(ref tank, input, Field.Terrain);
            TryFire(ref tank, input);
        }
        StepShells();
    }

    /// <summary>
    /// Inputs come from the network: clamp the axes, and replace a non-finite aim with the turret's current heading
    /// so a bad packet can't spin the turret.
    /// </summary>
    internal static InputCmd Sanitize(in InputCmd input, in TankState tank)
    {
        sbyte throttle = (sbyte)System.Math.Sign(input.Throttle);
        sbyte steer = (sbyte)System.Math.Sign(input.Steer);
        bool aimValid = float.IsFinite(input.AimYaw) && float.IsFinite(input.AimPitch);
        float aimYaw = aimValid ? input.AimYaw : Angles.Wrap(tank.Yaw + tank.TurretYaw);
        float aimPitch = aimValid ? input.AimPitch : 0f;
        return input with { Throttle = throttle, Steer = steer, AimYaw = aimYaw, AimPitch = aimPitch };
    }

    /// <summary>Every tank collides against where the others were at the start of the tick, independent of slot order.</summary>
    private void SnapshotFootprints()
    {
        for (int j = 0; j < TankCount; j++)
        {
            ref readonly TankState t = ref _tanks[j];
            _footprints[j] = TankMotion.Footprint(t.Spec, t.Position, t.Yaw);
        }
    }

    private ReadOnlySpan<Obb2> FootprintsExcept(int index)
    {
        int n = 0;
        for (int j = 0; j < TankCount; j++)
            if (j != index) _others[n++] = _footprints[j];
        return _others.AsSpan(0, n);
    }

    private void TryFire(ref TankState tank, in InputCmd input)
    {
        tank.Reload = MathF.Max(0, tank.Reload - SimConstants.Dt);
        if (!input.Fire || tank.Reload > 0 || ShellCount == MaxShells) return;

        TankPose pose = TankPose.Compute(tank, Field.Terrain);
        Vector3 trunnion = pose.Trunnion(tank.Spec);
        var shell = new Shell
        {
            Id = _nextId++,
            OwnerTankId = tank.Id,
            Position = pose.Muzzle(tank.Spec, tank.TurretYaw, tank.GunPitch),
            Velocity = pose.GunDirection(tank.TurretYaw, tank.GunPitch) * tank.Spec.ShellSpeed,
        };
        tank.Reload = tank.Spec.ReloadTime;
        tank.ShotCount++;
        _events.Add(new GameEvent(GameEventType.ShellFired, Tick, tank.Id, shell.Id, shell.Position, shell.Velocity));

        // Barrel pushed into a rock or the ground: the shell bursts where the barrel meets it instead of spawning inside.
        if (Field.Raycast(trunnion, shell.Position, out RayHit blocked))
        {
            _events.Add(new GameEvent(GameEventType.ShellExploded, Tick, tank.Id, shell.Id, blocked.Point, Vector3.Zero));
            return;
        }
        _shells[ShellCount++] = shell;
    }

    private void StepShells()
    {
        for (int i = ShellCount - 1; i >= 0; i--)
        {
            ref Shell shell = ref _shells[i];
            bool hit = Ballistics.Step(ref shell, Field, out RayHit rayHit);
            if (!hit && shell.Age < Ballistics.MaxLifetime) continue;
            if (hit)
                _events.Add(new GameEvent(GameEventType.ShellExploded, Tick, shell.OwnerTankId, shell.Id, rayHit.Point, Vector3.Zero));
            _shells[i] = _shells[--ShellCount];
        }
    }
}
