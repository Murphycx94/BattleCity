using Godot;
using Tank.Client.Art;
using Tank.Client.Combat;
using Tank.Client.Minimap;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;
using Side = Tank.Client.Match.Side;   // not Godot.Side
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// The practice targets, all of team <see cref="TargetTeam"/>: blue-grey tanks of every kind on the map's
/// <see cref="MapSite.Targets"/> that sit still with their guns straight ahead and watch nobody, and then, on a map whose
/// site <see cref="MapSite.HasPatrol"/> (the sandbox's), the patrolling target (stage 3 design §4.2), a light tank that a
/// <see cref="PatrolDriver"/> drives round <see cref="SandboxMap.PatrolRoute"/> from <see cref="SandboxMap.PatrolStart"/>,
/// firing short of the player from the bushes; while parked it holds still at its start instead. A knocked-out target
/// blows up, throws its turret (a tank destroyer has none), chars and smokes; ten seconds later it is revived where it
/// stands (the patrol then drives on).
/// A target the player's team doesn't see (<see cref="VisionTracker"/>) isn't drawn. Each <see cref="Sync"/> also notes
/// every target's head point, health and whether it is shown, for the HUD, and where it is drawn, for the map
/// (<see cref="FillMinimap"/>).
/// </summary>
public partial class TargetRange : Node3D
{
    public const byte PlayerTeam = 0, TargetTeam = 1;
    public const float ReviveSeconds = 10f;
    private const float BlastSize = 1.8f;
    private static readonly TankPaint Paint = new(Palette.TargetBody, Palette.Track, Palette.TankHub);

    private readonly List<Target> _targets = new();
    private VisionTracker _vision = null!;
    private PatrolDriver? _driver;   // null without a patrol
    private bool _patrolParked;

    private sealed class Target
    {
        public required int Id;
        public required int Slot;   // index in Simulation.Tanks
        public required float Yaw;  // the heading it was placed with: a still target's gun stays straight ahead
        public required TankActor Actor;   // the patrol's alone kicks up dust
        public TankState Previous, Current;   // the last two ticks, drawn between
        public SN.Vector2 Drawn;   // where the last Sync drew it (x, z), for the map
        public int WreckedTick = -1;
        public float Health = 1f;   // share left, 0 … 1
        public bool Alive = true;
        public bool Shown = true;
    }

    /// <summary>How many targets there are: the still ones, in the site's <see cref="MapSite.Targets"/> order, then the patrol if any.</summary>
    public int Count => _targets.Count;

    /// <summary>The index of the patrolling target, after the still ones; −1 on a map without one.</summary>
    public int PatrolIndex { get; private set; } = -1;

    /// <summary>
    /// Adds <paramref name="site"/>'s targets to the simulation (after the player, so they take the next slots), tells
    /// <paramref name="vision"/> the still ones watch nobody, and builds their models; then the patrol, if the site has
    /// one. With <paramref name="parkPatrol"/> the patrol stays at its start, holds its fire and watches nobody either
    /// (range demos other than the spotting one, so their screenshots stay as they were, the spotted lamp included).
    /// </summary>
    public void Build(Simulation sim, VisionTracker vision, bool parkPatrol, MapSite site)
    {
        (_vision, _patrolParked) = (vision, parkPatrol);
        foreach (TargetSpot spot in site.Targets)
        {
            Target target = Add(sim, spot.Spec, spot.Position, spot.Yaw);
            vision.SetWatching(target.Id, false);
        }
        if (!site.HasPatrol) return;
        PatrolIndex = _targets.Count;
        _driver = new PatrolDriver(SandboxMap.PatrolRoute);
        SN.Vector2 start = SandboxMap.PatrolStart;
        Target patrol = Add(sim, SandboxMap.PatrolSpec, start, Angles.YawOf(SandboxMap.PatrolRoute[0].Position - start), dust: true);
        if (parkPatrol) vision.SetWatching(patrol.Id, false);
    }

    private Target Add(Simulation sim, TankSpec spec, SN.Vector2 position, float yaw, bool dust = false)
    {
        int slot = sim.TankCount;
        int id = sim.AddTank(spec, position, yaw, TargetTeam);
        var actor = new TankActor();
        AddChild(actor);
        actor.Build(spec, Paint, dust);
        var target = new Target { Id = id, Slot = slot, Yaw = yaw, Actor = actor, Previous = sim.Tanks[slot], Current = sim.Tanks[slot], Drawn = sim.Tanks[slot].Position };
        actor.Present(target.Current, Observe(target, target.Current, sim.Field.Terrain), 0f, 0f, shown: true);   // heads are known before the first Sync
        _targets.Add(target);
        return target;
    }

    /// <summary>Target <paramref name="index"/>'s head point (where its health bar goes), health left (0 … 1), whether it is alive and whether it is drawn.</summary>
    public (Vector3 Head, float Health, bool Alive, bool Shown) Status(int index)
    {
        Target target = _targets[index];
        return (target.Actor.Head, target.Health, target.Alive, target.Shown);
    }

    /// <summary>Target <paramref name="index"/>'s tank id.</summary>
    public int IdAt(int index) => _targets[index].Id;

    /// <summary>The point over target <paramref name="tankId"/>'s turret, for damage numbers; false for a tank that isn't a target.</summary>
    public bool TryGetHead(int tankId, out Vector3 head)
    {
        Target? target = Find(tankId);
        head = target?.Actor.Head ?? Vector3.Zero;
        return target is not null;
    }

    /// <summary>True for a target the player's team doesn't see now: it isn't drawn, nor are verdicts and damage numbers for hits on it.</summary>
    public bool IsHidden(int tankId) => Find(tankId) is not null && !_vision.IsVisible(tankId, PlayerTeam);

    /// <summary>
    /// The targets' commands for <paramref name="tick"/>; <paramref name="commands"/> starts at the first target's slot.
    /// The still ones hold with their guns straight ahead; the patrol (if there is one) drives, firing short of
    /// <paramref name="player"/>, unless parked.
    /// </summary>
    public void FillInputs(Span<InputCmd> commands, int tick, Simulation sim, SN.Vector2 player)
    {
        for (int i = 0; i < _targets.Count; i++) commands[i] = new InputCmd(tick, 0, 0, _targets[i].Yaw, 0f, false);
        if (_driver is not null && !_patrolParked)
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
    /// Revives wrecks that have lain for <see cref="ReviveSeconds"/>, and restocks the patrol (if any) once its AP racks are empty.
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
        if (PatrolIndex < 0) return;
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
            target.Actor.Wreck(sim.Tanks[target.Slot].TurretYaw);
            fx.Explosion(e.Position.ToGodot() + Vector3.Up * 1.2f, PuffColor.White, BlastSize);
        }
        else if (e.Type == GameEventType.TankRevived)
        {
            target.Actor.Restore();
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
            target.Drawn = state.Position;
            bool shown = _vision.IsVisible(target.Id, PlayerTeam);
            target.Shown = shown;
            TankPose pose = Observe(target, state, sim.Field.Terrain);
            float yawRate = Angles.Wrap(target.Current.Yaw - target.Previous.Yaw) / SimConstants.Dt;
            target.Actor.Present(state, pose, yawRate, delta, shown);   // a hidden patrol leaves no trail, to pop in when it shows
        }
    }

    /// <summary>
    /// The map's icons for the targets (stage 5 design §5): every one the player's team sees (not <see cref="IsHidden"/>),
    /// as an enemy of its kind, where the last <see cref="Sync"/> drew it, a wreck while knocked out. Call after
    /// <see cref="Sync"/>; allocates nothing.
    /// </summary>
    public void FillMinimap(MinimapFrame frame)
    {
        foreach (Target target in _targets)
        {
            if (!_vision.IsVisible(target.Id, PlayerTeam)) continue;   // IsHidden, without the lookup
            frame.AddTank(new MinimapTank(target.Drawn, Side.Enemy, MinimapFeed.ShapeOf(target.Current.Spec), Destroyed: !target.Alive));
        }
    }

    /// <summary>Notes the target's health from <paramref name="state"/>; returns its pose.</summary>
    private static TankPose Observe(Target target, in TankState state, HeightField terrain)
    {
        TankPose pose = TankPose.Compute(state, terrain);
        target.Health = state.Health / (float)state.Spec.MaxHealth;
        target.Alive = !state.IsDestroyed;
        return pose;
    }

    private Target? Find(int tankId)
    {
        foreach (Target t in _targets)
            if (t.Id == tankId) return t;
        return null;
    }
}
