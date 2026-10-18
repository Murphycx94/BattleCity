using Godot;
using Tank.Client.Art;
using Tank.Client.Combat;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// The practice targets, all of team <see cref="TargetTeam"/>: blue-grey tanks of every kind on
/// <see cref="SandboxMap.TargetSpots"/> that sit still with their guns straight ahead and watch nobody, and then the
/// patrolling target (stage 3 design §4.2), a light tank that a <see cref="PatrolDriver"/> drives round
/// <see cref="SandboxMap.PatrolRoute"/> from <see cref="SandboxMap.PatrolStart"/>, firing short of the player from the
/// bushes; while parked it holds still at its start instead. A knocked-out target blows up, throws its turret (a tank
/// destroyer has none), chars and smokes; ten seconds later it is revived where it stands (the patrol then drives on).
/// A target the player's team doesn't see (<see cref="VisionTracker"/>) isn't drawn. Each <see cref="Sync"/> also notes
/// every target's head point, health and whether it is shown, for the HUD.
/// </summary>
public partial class TargetRange : Node3D
{
    public const byte PlayerTeam = 0, TargetTeam = 1;
    public const float ReviveSeconds = 10f;
    private const float BlastSize = 1.8f;
    private const float HeadMargin = 0.6f;   // the head point floats this far above the turret's armour box
    private static readonly TankPaint Paint = new(Palette.TargetBody, Palette.Track, Palette.TankHub);

    private readonly List<Target> _targets = new();
    private VisionTracker _vision = null!;
    private PatrolDriver _driver = null!;
    private bool _patrolParked;

    private sealed class Target
    {
        public required int Id;
        public required int Slot;   // index in Simulation.Tanks
        public required float Yaw;  // the heading it was placed with: a still target's gun stays straight ahead
        public required TankModel Model;
        public required WreckSmoke Smoke;
        public DustTrail? Dust;     // the patrol's only
        public TankState Previous, Current;   // the last two ticks, drawn between
        public int WreckedTick = -1;
        public Vector3 Head;        // world point over the turret, as of the latest Sync
        public float Health = 1f;   // share left, 0 … 1
        public bool Alive = true;
        public bool Shown = true;
    }

    /// <summary>How many targets there are: the still ones, in <see cref="SandboxMap.TargetSpots"/> order, then the patrol.</summary>
    public int Count => _targets.Count;

    /// <summary>The index of the patrolling target, after the still ones.</summary>
    public static int PatrolIndex => SandboxMap.TargetSpots.Count;

    /// <summary>
    /// Adds the targets to the simulation (after the player, so they take the next slots), tells
    /// <paramref name="vision"/> the still ones watch nobody, and builds their models. With <paramref name="parkPatrol"/>
    /// the patrol stays at its start, holds its fire and watches nobody either (range demos other than the spotting one,
    /// so their screenshots stay as they were, the spotted lamp included).
    /// </summary>
    public void Build(Simulation sim, VisionTracker vision, bool parkPatrol)
    {
        (_vision, _patrolParked) = (vision, parkPatrol);
        foreach (TargetSpot spot in SandboxMap.TargetSpots)
        {
            Target target = Add(sim, spot.Spec, spot.Position, spot.Yaw);
            vision.SetWatching(target.Id, false);
        }
        _driver = new PatrolDriver(SandboxMap.PatrolRoute);
        SN.Vector2 start = SandboxMap.PatrolStart;
        Target patrol = Add(sim, SandboxMap.PatrolSpec, start, Angles.YawOf(SandboxMap.PatrolRoute[0].Position - start));
        if (parkPatrol) vision.SetWatching(patrol.Id, false);
        patrol.Dust = new DustTrail();
        patrol.Model.AddChild(patrol.Dust);
        patrol.Dust.Build(TankLooks.For(SandboxMap.PatrolSpec.Id).TrackX, SandboxMap.PatrolSpec.HullLength / 2f);
    }

    private Target Add(Simulation sim, TankSpec spec, SN.Vector2 position, float yaw)
    {
        int slot = sim.TankCount;
        int id = sim.AddTank(spec, position, yaw, TargetTeam);
        var model = new TankModel();
        AddChild(model);
        model.Build(spec, Paint);
        var smoke = new WreckSmoke();
        model.AddChild(smoke);
        smoke.Build(model.TurretRing);   // pours from the open turret ring (a casemate's roof)
        var target = new Target { Id = id, Slot = slot, Yaw = yaw, Model = model, Smoke = smoke, Previous = sim.Tanks[slot], Current = sim.Tanks[slot] };
        Observe(target, target.Current, sim.Field.Terrain);   // heads are known before the first Sync
        _targets.Add(target);
        return target;
    }

    /// <summary>Target <paramref name="index"/>'s head point (where its health bar goes), health left (0 … 1), whether it is alive and whether it is drawn.</summary>
    public (Vector3 Head, float Health, bool Alive, bool Shown) Status(int index)
    {
        Target target = _targets[index];
        return (target.Head, target.Health, target.Alive, target.Shown);
    }

    /// <summary>Target <paramref name="index"/>'s tank id.</summary>
    public int IdAt(int index) => _targets[index].Id;

    /// <summary>The point over target <paramref name="tankId"/>'s turret, for damage numbers; false for a tank that isn't a target.</summary>
    public bool TryGetHead(int tankId, out Vector3 head)
    {
        Target? target = Find(tankId);
        head = target?.Head ?? Vector3.Zero;
        return target is not null;
    }

    /// <summary>True for a target the player's team doesn't see now: it isn't drawn, nor are verdicts and damage numbers for hits on it.</summary>
    public bool IsHidden(int tankId) => Find(tankId) is not null && !_vision.IsVisible(tankId, PlayerTeam);

    /// <summary>
    /// The targets' commands for <paramref name="tick"/>; <paramref name="commands"/> starts at the first target's slot.
    /// The still ones hold with their guns straight ahead; the patrol drives, firing short of <paramref name="player"/>,
    /// unless parked.
    /// </summary>
    public void FillInputs(Span<InputCmd> commands, int tick, Simulation sim, SN.Vector2 player)
    {
        for (int i = 0; i < _targets.Count; i++) commands[i] = new InputCmd(tick, 0, 0, _targets[i].Yaw, 0f, false);
        if (!_patrolParked)
        {
            Target patrol = _targets[PatrolIndex];
            commands[PatrolIndex] = _driver.Command(sim.Tanks[patrol.Slot], sim.Field, tick, player);
        }
    }

    /// <summary>Notes every target's newest state, keeping the one before to draw between. Call after each step.</summary>
    public void AfterStep(Simulation sim)
    {
        foreach (Target t in _targets) (t.Previous, t.Current) = (t.Current, sim.Tanks[t.Slot]);
    }

    /// <summary>
    /// Revives wrecks that have lain for <see cref="ReviveSeconds"/>, and restocks the patrol once its AP racks are empty.
    /// Call before stepping the simulation.
    /// </summary>
    public void ReviveDue(Simulation sim)
    {
        int due = (int)MathF.Round(ReviveSeconds * SimConstants.TickRate);
        foreach (Target t in _targets)
            if (t.WreckedTick >= 0 && sim.Tick - t.WreckedTick >= due)
            {
                sim.Revive(t.Id);
                t.WreckedTick = -1;
            }
        Target patrol = _targets[PatrolIndex];
        if (sim.Tanks[patrol.Slot].ApLeft == 0) sim.Restock(patrol.Id);   // it fires AP for as long as the sandbox runs
    }

    /// <summary>Reacts to <see cref="GameEventType.TankDestroyed"/> and <see cref="GameEventType.TankRevived"/> for targets.</summary>
    public void OnEvent(in GameEvent e, Simulation sim, BattleFx fx)
    {
        Target? target = Find(e.TargetTankId);
        if (target is null) return;
        if (e.Type == GameEventType.TankDestroyed)
        {
            target.WreckedTick = e.Tick;
            target.Model.Wreck(sim.Tanks[target.Slot].TurretYaw);
            target.Smoke.Emitting = true;
            fx.Explosion(e.Position.ToGodot() + Vector3.Up * 1.2f, PuffColor.White, BlastSize);
        }
        else if (e.Type == GameEventType.TankRevived)
        {
            target.Model.Restore();
            target.Smoke.Emitting = false;
        }
    }

    /// <summary>
    /// Shows the targets the player's team sees, each <paramref name="alpha"/> of the way from its previous tick to its
    /// latest, runs their animations and notes their heads, health and visibility.
    /// </summary>
    public void Sync(Simulation sim, float alpha, float delta)
    {
        foreach (Target target in _targets)
        {
            TankState state = TankInterpolation.Between(target.Previous, target.Current, alpha);
            bool shown = _vision.IsVisible(target.Id, PlayerTeam);
            if (shown != target.Shown) target.Model.Visible = target.Shown = shown;
            target.Model.Apply(Observe(target, state, sim.Field.Terrain), state.TurretYaw, state.GunPitch);
            float yawRate = Angles.Wrap(target.Current.Yaw - target.Previous.Yaw) / SimConstants.Dt;
            target.Model.Animate(state.Speed, yawRate, delta);
            target.Dust?.Update(shown ? state.Speed : 0f);   // no trail from a hidden patrol, to pop in when it shows
        }
    }

    /// <summary>Notes the target's head point and health from <paramref name="state"/>; returns its pose.</summary>
    private static TankPose Observe(Target target, in TankState state, HeightField terrain)
    {
        TankPose pose = TankPose.Compute(state, terrain);
        target.Head = (pose.Position + SN.Vector3.UnitY * HeadHeight(state.Spec)).ToGodot();
        target.Health = state.Health / (float)state.Spec.MaxHealth;
        target.Alive = !state.IsDestroyed;
        return pose;
    }

    /// <summary>Height of the head point over the hull origin: a margin above the turret's armour box (3.4 m on the medium tank).</summary>
    private static float HeadHeight(TankSpec spec) => spec.TurretArmor.Top + HeadMargin;

    private Target? Find(int tankId)
    {
        foreach (Target t in _targets)
            if (t.Id == tankId) return t;
        return null;
    }
}
