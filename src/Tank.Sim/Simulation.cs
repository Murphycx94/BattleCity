using System.Numerics;
using System.Runtime.InteropServices;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim;

/// <summary>
/// Advances the battle by one fixed 30Hz tick. Order inside a tick: tank footprints are snapshotted; each tank drives,
/// aims, updates its aim circle and fires (in slot order) against that snapshot, touching no other tank, so its turn
/// doesn't depend on its slot. Then the shell phase: shots that burst in a blocked barrel go off, then every shell in
/// flight flies and resolves its hits, each in firing order (shots of the same tick in slot order).
/// </summary>
public sealed partial class Simulation
{
    public const int MaxTanks = 32;
    public const int MaxShells = 256;

    /// <summary>Float drift in the countdown must not cost an extra tick of reload.</summary>
    private const float ReloadEpsilon = 1e-4f;

    private readonly TankState[] _tanks = new TankState[MaxTanks];
    private readonly Shell[] _shells = new Shell[MaxShells];
    private readonly Obb2[] _footprints = new Obb2[MaxTanks];
    private readonly Obb2[] _others = new Obb2[MaxTanks];

    /// <summary>Sized for a busy tick (every tank firing, plus some hits) so a normal step never grows it.</summary>
    private readonly List<GameEvent> _events = new(MaxTanks * 2);

    /// <summary>Events raised between steps (<see cref="Revive"/>); the next <see cref="Step"/> reports them.</summary>
    private readonly List<GameEvent> _pending = new(4);

    private int _nextId = 1;

    public Simulation(Battlefield field) => Field = field;

    public Battlefield Field { get; }
    public int Tick { get; private set; }
    public int TankCount { get; private set; }
    public int ShellCount { get; private set; }
    public ReadOnlySpan<TankState> Tanks => _tanks.AsSpan(0, TankCount);
    public ReadOnlySpan<Shell> Shells => _shells.AsSpan(0, ShellCount);

    /// <summary>Events produced by the most recent <see cref="Step"/>.</summary>
    public ReadOnlySpan<GameEvent> Events => CollectionsMarshal.AsSpan(_events);

    /// <summary>Adds a tank with full health and ammunition, AP loaded.</summary>
    /// <returns>The new tank's id. It occupies the next slot in <see cref="Tanks"/>.</returns>
    public int AddTank(TankSpec spec, Vector2 position, float yaw)
    {
        if (TankCount == MaxTanks) throw new InvalidOperationException($"At most {MaxTanks} tanks.");
        int id = _nextId++;
        _tanks[TankCount++] = new TankState
        {
            Id = id,
            Spec = spec,
            Position = Field.ClampToPlayable(position),
            Yaw = Angles.Wrap(yaw),
            Health = spec.MaxHealth,
            ApLeft = spec.Ap.Capacity,
            HeLeft = spec.He.Capacity,
        };
        return id;
    }

    /// <param name="inputs">One command per tank, in <see cref="Tanks"/> order.</param>
    public void Step(ReadOnlySpan<InputCmd> inputs)
    {
        if (inputs.Length != TankCount)
            throw new ArgumentException($"Expected {TankCount} inputs, got {inputs.Length}.", nameof(inputs));
        _events.Clear();
        Tick++;
        foreach (GameEvent e in _pending) _events.Add(e with { Tick = Tick });
        _pending.Clear();
        SnapshotFootprints();
        for (int i = 0; i < TankCount; i++)
        {
            ref TankState tank = ref _tanks[i];
            if (tank.IsDestroyed) continue;   // wrecks don't drive, aim or fire, but still block (see SnapshotFootprints)
            InputCmd input = Sanitize(inputs[i], tank);
            float yaw = tank.Yaw, turretYaw = tank.TurretYaw;
            TankMotion.Drive(ref tank, input, Field, FootprintsExcept(i));
            TankMotion.AimGun(ref tank, input, Field.Terrain);
            UpdateBloom(ref tank, yaw, turretYaw);
            TryFire(ref tank, input);
        }
        StepShells();
    }

    /// <summary>
    /// Brings a wreck back where it stands: full health, aim circle closed, gun loaded (sandbox only for now). The
    /// <see cref="GameEventType.TankRevived"/> event is reported by the next <see cref="Step"/>.
    /// </summary>
    public void Revive(int tankId)
    {
        ref TankState tank = ref TankRef(tankId);
        tank.Health = tank.Spec.MaxHealth;
        tank.IsDestroyed = false;
        tank.Bloom = 0f;
        tank.Reload = 0f;
        tank.Speed = 0f;
        Vector3 at = TankPose.Compute(tank, Field.Terrain).Position;
        _pending.Add(new GameEvent(GameEventType.TankRevived, Tick, tankId, 0, at, Vector3.Zero, TargetTankId: tankId));
    }

    /// <summary>Refills both ammunition racks (sandbox only for now).</summary>
    public void Restock(int tankId)
    {
        ref TankState tank = ref TankRef(tankId);
        tank.ApLeft = tank.Spec.Ap.Capacity;
        tank.HeLeft = tank.Spec.He.Capacity;
    }

    internal ref TankState TankRef(int tankId)
    {
        for (int i = 0; i < TankCount; i++)
            if (_tanks[i].Id == tankId) return ref _tanks[i];
        throw new ArgumentException($"No tank {tankId}.", nameof(tankId));
    }

    /// <summary>
    /// Inputs come from the network: clamp the axes, replace a non-finite aim with the turret's current heading so a bad
    /// packet can't spin the turret, and keep the loaded round if the requested one isn't a known type.
    /// </summary>
    internal static InputCmd Sanitize(in InputCmd input, in TankState tank)
    {
        sbyte throttle = (sbyte)System.Math.Sign(input.Throttle);
        sbyte steer = (sbyte)System.Math.Sign(input.Steer);
        bool aimValid = float.IsFinite(input.AimYaw) && float.IsFinite(input.AimPitch);
        float aimYaw = aimValid ? input.AimYaw : Angles.Wrap(tank.Yaw + tank.TurretYaw);
        float aimPitch = aimValid ? input.AimPitch : 0f;
        AmmoType ammo = input.Ammo is AmmoType.AP or AmmoType.HE ? input.Ammo : tank.Ammo;
        return input with { Throttle = throttle, Steer = steer, AimYaw = aimYaw, AimPitch = aimPitch, Ammo = ammo };
    }

    /// <summary>Opens the aim circle for this tick's driving, hull turn and turret turn, or lets it settle.</summary>
    private static void UpdateBloom(ref TankState tank, float oldYaw, float oldTurretYaw)
    {
        float hullRate = Angles.Wrap(tank.Yaw - oldYaw) / SimConstants.Dt;
        float turretRate = Angles.Wrap(tank.TurretYaw - oldTurretYaw) / SimConstants.Dt;
        float target = Dispersion.Target(tank.Spec, tank.Speed, hullRate, turretRate);
        tank.Bloom = Dispersion.Step(tank.Bloom, target, tank.Spec.AimTime);
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

    /// <summary>
    /// Counts the reload down, swaps rounds if the input asks for the other type (which restarts the reload), then fires
    /// if asked, loaded and a round of that type is left. The shell leaves inside the aim circle, which then opens.
    /// </summary>
    private void TryFire(ref TankState tank, in InputCmd input)
    {
        tank.Reload = MathF.Max(0, tank.Reload - SimConstants.Dt);
        if (tank.Reload < ReloadEpsilon) tank.Reload = 0f;
        if (input.Ammo != tank.Ammo)
        {
            tank.Ammo = input.Ammo;
            tank.Reload = tank.Spec.ReloadTime;
            return;
        }
        if (!input.Fire || tank.Reload > 0 || tank.AmmoLeft(tank.Ammo) == 0 || ShellCount == MaxShells) return;

        Ballistics.Launch launch = Ballistics.Aim(tank, Field);
        uint seed = ShotRandom.Seed(Tick, tank.Id, tank.ShotCount);
        float speed = launch.Velocity.Length();
        var shell = new Shell
        {
            Id = _nextId++,
            OwnerTankId = tank.Id,
            Position = launch.Origin,
            Velocity = Dispersion.Deflect(launch.Velocity, Dispersion.Radius(tank.Spec, tank.Bloom), seed) * speed,
            Ammo = tank.Ammo,
            Round = tank.Spec.Round(tank.Ammo),
            Caliber = tank.Spec.Caliber,
            Seed = seed,
        };
        tank.Reload = tank.Spec.ReloadTime;
        tank.ShotCount++;
        tank.Bloom += tank.Spec.Bloom.Fire;
        if (tank.Ammo == AmmoType.HE) tank.HeLeft--;
        else tank.ApLeft--;
        _events.Add(new GameEvent(GameEventType.ShellFired, Tick, tank.Id, shell.Id, shell.Position, shell.Velocity, Ammo: shell.Ammo));

        // Barrel pushed into a rock or the ground: the shell bursts where the barrel meets it instead of spawning inside.
        // It goes off in the shell phase, so every tank has had its turn before the splash, whatever the slots.
        if (launch.Blocked)
        {
            _barrelBursts[_barrelBurstCount++] = new BarrelBurst(shell, launch.BlockPoint);
            return;
        }
        _shells[ShellCount++] = shell;
    }
}
