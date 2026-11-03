using System.Numerics;
using System.Runtime.InteropServices;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim;

/// <summary>
/// Advances the battle by one fixed 30Hz tick. Order inside a tick: tank footprints are snapshotted; each tank drives
/// (a casemate gun's hull turning by itself towards an aim outside the arc, knocking down fences and, rammed fast
/// enough, brick walls in its way), aims, updates its aim circle and fires (in slot order) against that snapshot,
/// touching no other tank, so its turn doesn't depend on its slot (except that when the shell pool is full lower slots
/// get to fire first, shell ids follow slot order, and a prop one tank knocks down is already down for the next). Then
/// the shell phase: shots that burst in a blocked barrel go off, then every shell in flight flies and resolves its hits,
/// each group in firing order (shots of the same tick in slot order).
/// <para>
/// Tanks sit in slots in <see cref="Tanks"/>, in the order they were added; <see cref="RemoveTank"/> closes the gap, so
/// the slots after it move down by one (ids never change or come back). Hosts that keep per-slot data follow
/// <see cref="SlotOf"/>, as <see cref="Vision.VisionTracker"/> does.
/// </para>
/// </summary>
public sealed partial class Simulation
{
    public const int MaxTanks = 32;
    public const int MaxShells = 256;

    private readonly TankState[] _tanks = new TankState[MaxTanks];
    private readonly Shell[] _shells = new Shell[MaxShells];
    private readonly Obb2[] _footprints = new Obb2[MaxTanks];
    private readonly Obb2[] _others = new Obb2[MaxTanks];

    /// <summary>
    /// Sized for a busy tick (every tank firing, plus hits, and an HE burst in a bush patch, which alone can report a dozen
    /// props down) so a normal step never grows it.
    /// </summary>
    private readonly List<GameEvent> _events = new(MaxTanks * 4);

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

    /// <summary>Adds a tank of team <paramref name="team"/> (below <see cref="SimConstants.MaxTeams"/>) with full health and ammunition, AP loaded.</summary>
    /// <returns>The new tank's id. It occupies the next slot in <see cref="Tanks"/>.</returns>
    public int AddTank(TankSpec spec, Vector2 position, float yaw, byte team = 0)
    {
        if (TankCount == MaxTanks) throw new InvalidOperationException($"At most {MaxTanks} tanks.");
        if (team >= SimConstants.MaxTeams) throw new ArgumentOutOfRangeException(nameof(team), team, $"Teams are 0 … {SimConstants.MaxTeams - 1}.");
        int id = _nextId++;
        _tanks[TankCount++] = Fresh(id, spec, position, yaw, team);
        return id;
    }

    /// <summary>The slot of tank <paramref name="tankId"/> in <see cref="Tanks"/>, or −1 when there is no such tank.</summary>
    public int SlotOf(int tankId)
    {
        for (int i = 0; i < TankCount; i++)
            if (_tanks[i].Id == tankId) return i;
        return -1;
    }

    /// <summary>
    /// Takes a tank out of the battle (its player left): the slots after it move down by one. Shells it fired fly on and
    /// still count as its. No event: the host knows.
    /// </summary>
    public void RemoveTank(int tankId)
    {
        int slot = SlotOf(tankId);
        if (slot < 0) throw new ArgumentException($"No tank {tankId}.", nameof(tankId));
        Array.Copy(_tanks, slot + 1, _tanks, slot, TankCount - slot - 1);
        TankCount--;
        _tanks[TankCount] = default;
    }

    /// <summary>
    /// Puts a tank (a wreck, usually) back into the battle at <paramref name="position"/> facing <paramref name="yaw"/>,
    /// as fresh as on <see cref="AddTank"/>: full health and ammunition, AP loaded, standing still, not having fired.
    /// Its id, team and shot count stay (the shot count carries on, so each tank's shot index stays monotonic). The
    /// <see cref="GameEventType.TankRevived"/> event is reported by the next <see cref="Step"/>; the host sets any
    /// protection with <see cref="Protect"/>.
    /// </summary>
    public void Respawn(int tankId, Vector2 position, float yaw)
    {
        ref TankState tank = ref TankRef(tankId);
        tank = Fresh(tank.Id, tank.Spec, position, yaw, tank.Team) with { ShotCount = tank.ShotCount };
        Vector3 at = TankPose.Compute(tank, Field.Terrain).Position;
        _pending.Add(new GameEvent(GameEventType.TankRevived, Tick, tankId, 0, at, Vector3.Zero, TargetTankId: tankId));
    }

    /// <summary>Protects a tank through the next <paramref name="ticks"/> steps (see <see cref="TankState.ProtectedUntil"/>).</summary>
    public void Protect(int tankId, int ticks) => TankRef(tankId).ProtectedUntil = Tick + ticks + 1;

    /// <summary>Whether <paramref name="tank"/> is under spawn protection at the current tick.</summary>
    public bool IsProtected(in TankState tank) => Tick < tank.ProtectedUntil;

    /// <summary>
    /// Makes a tank another kind of vehicle where it stands (sandbox only for now). Its id, team, place, heading and shot
    /// count stay (the shot count carries on, so each tank's shot index stays monotonic); everything else starts as on
    /// <see cref="AddTank"/>, a wreck included, and it has not fired yet. Shells already in flight keep their own round. No event: the caller knows.
    /// </summary>
    public void ChangeSpec(int tankId, TankSpec spec)
    {
        ref TankState tank = ref TankRef(tankId);
        tank = Fresh(tank.Id, spec, tank.Position, tank.Yaw, tank.Team) with { ShotCount = tank.ShotCount };
    }

    /// <summary>A tank at rest with full health and ammunition, AP loaded, that hasn't fired.</summary>
    private TankState Fresh(int id, TankSpec spec, Vector2 position, float yaw, byte team) => new()
    {
        Id = id,
        Spec = spec,
        Team = team,
        LastFired = TankState.NeverFired,
        Position = Field.ClampToPlayable(position),
        Yaw = Angles.Wrap(yaw),
        Health = spec.MaxHealth,
        ApLeft = spec.Ap.Capacity,
        HeLeft = spec.He.Capacity,
    };

    /// <summary>
    /// One tick: pending events first, then every live tank's step (its props felled, then its shot), then the shells.
    /// </summary>
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
            bool fired = TankStep.Advance(ref tank, inputs[i], Tick, Field, FootprintsExcept(i), knockDown: true, _felled,
                canFire: !IsProtected(tank) && ShellCount < MaxShells, out TankStep.Shot shot);
            ReportFelled(tank);
            if (fired) Launch(tank, shot);
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

    /// <summary>
    /// Takes <paramref name="damage"/> off live tank <paramref name="tankId"/> whatever its protection (the royale's zone: stage 6
    /// design §3.2). At 0 it becomes a wreck and a <see cref="GameEventType.TankDestroyed"/> event (<c>TankId</c> 0, <c>ShellId</c> 0,
    /// <see cref="KillCause.Zone"/>, <c>Position</c> the wreck) joins this step's <see cref="Events"/>: call it after <see cref="Step"/>,
    /// before the events are read. No <see cref="GameEventType.TankHit"/> event. A wreck, or <paramref name="damage"/> ≤ 0: nothing.
    /// </summary>
    /// <exception cref="ArgumentException">No such tank.</exception>
    public void HurtByZone(int tankId, int damage) => Damage(ref TankRef(tankId), damage, 0, 0, KillCause.Zone);

    /// <summary>Refills both ammunition racks (sandbox only for now).</summary>
    public void Restock(int tankId)
    {
        ref TankState tank = ref TankRef(tankId);
        tank.ApLeft = tank.Spec.Ap.Capacity;
        tank.HeLeft = tank.Spec.He.Capacity;
    }

    /// <summary>
    /// Applies <paramref name="crate"/> to the live tank <paramref name="tankId"/> (stage 6 design §4.2) and emits a
    /// <see cref="GameEventType.CratePicked"/> event (<c>TankId</c> = tankId, <c>PropId</c> = crate.Id, <c>Damage</c> = the take's
    /// <see cref="CrateTake.Amount"/>, <c>Position</c> = the crate on the ground). The event joins this step's <see cref="Events"/>
    /// (as <see cref="HurtByZone"/>'s does): call it after <see cref="Step"/>, before the events are read. A wreck takes nothing
    /// (a take with Amount 0, no event).
    /// </summary>
    /// <exception cref="ArgumentException">No such tank.</exception>
    public CrateTake TakeCrate(int tankId, in Crate crate)
    {
        ref TankState tank = ref TankRef(tankId);
        if (tank.IsDestroyed) return new CrateTake(crate.Kind, 0, 0, 0);
        CrateTake take = ApplyCrate(ref tank, crate.Kind);
        Vector2 p = crate.Position;
        var at = new Vector3(p.X, Field.Terrain.HeightAt(p.X, p.Y), p.Y);
        _events.Add(new GameEvent(GameEventType.CratePicked, Tick, tankId, 0, at, Vector3.Zero, Damage: take.Amount, PropId: crate.Id));
        return take;
    }

    /// <summary>
    /// What a crate of <paramref name="kind"/> does for a live tank: repair tops health up by <see cref="CrateRules.RepairAmount"/>
    /// (to full at most), ammo tops both racks up by <see cref="CrateRules.AmmoAmount"/> of each, the other three add a layer up to
    /// <see cref="CrateRules.MaxLayers"/>.
    /// </summary>
    private static CrateTake ApplyCrate(ref TankState tank, CrateKind kind)
    {
        switch (kind)
        {
            case CrateKind.Repair:
                int gain = Math.Min(CrateRules.RepairAmount(tank.Spec.MaxHealth), tank.Spec.MaxHealth - tank.Health);
                tank.Health += gain;
                return new CrateTake(kind, gain, 0, 0);
            case CrateKind.Ammo:
                int ap = Math.Min(CrateRules.AmmoAmount(tank.Spec.Ap.Capacity), tank.Spec.Ap.Capacity - tank.ApLeft);
                int he = Math.Min(CrateRules.AmmoAmount(tank.Spec.He.Capacity), tank.Spec.He.Capacity - tank.HeLeft);
                tank.ApLeft += ap;
                tank.HeLeft += he;
                return new CrateTake(kind, ap, he, 0);
            case CrateKind.Armor:
                return new CrateTake(kind, AddLayer(ref tank.ArmorLayers), 0, tank.ArmorLayers);
            case CrateKind.Reload:
                return new CrateTake(kind, AddLayer(ref tank.ReloadLayers), 0, tank.ReloadLayers);
            case CrateKind.Optics:
                return new CrateTake(kind, AddLayer(ref tank.OpticsLayers), 0, tank.OpticsLayers);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown crate kind.");
        }
    }

    /// <summary>Adds a layer below <see cref="CrateRules.MaxLayers"/>: 1 when it did, 0 when the tank had them all.</summary>
    private static int AddLayer(ref byte layers)
    {
        if (layers >= CrateRules.MaxLayers) return 0;
        layers++;
        return 1;
    }

    internal ref TankState TankRef(int tankId)
    {
        for (int i = 0; i < TankCount; i++)
            if (_tanks[i].Id == tankId) return ref _tanks[i];
        throw new ArgumentException($"No tank {tankId}.", nameof(tankId));
    }

    /// <summary>See <see cref="TankStep.Sanitize"/>.</summary>
    internal static InputCmd Sanitize(in InputCmd input, in TankState tank) => TankStep.Sanitize(input, tank);

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
    /// The shell a tank just fired (<see cref="TankStep.Advance"/>) gets its id and leaves the muzzle, or, when the barrel
    /// was pushed into a rock, a wall or the ground, bursts where the barrel meets it instead of spawning inside (a wall
    /// takes it as a direct hit). That goes off in the shell phase, so every tank has had its turn before the splash,
    /// whatever the slots.
    /// </summary>
    private void Launch(in TankState tank, in TankStep.Shot shot)
    {
        Shell shell = shot.Shell with { Id = _nextId++ };
        _events.Add(new GameEvent(GameEventType.ShellFired, Tick, tank.Id, shell.Id, shell.Position, shell.Velocity, Ammo: shell.Ammo));
        if (shot.Launch.Blocked)
        {
            _barrelBursts[_barrelBurstCount++] = new BarrelBurst(shell, shot.Launch.BlockPoint, shot.Launch.BlockPropId);
            return;
        }
        _shells[ShellCount++] = shell;
    }
}
