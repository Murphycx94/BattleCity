using Godot;
using Tank.Client.Art;
using Tank.Client.Combat;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// The six practice targets on <see cref="SandboxMap.TargetSpots"/>: blue-grey tanks of every kind that sit still with
/// their guns straight ahead. A knocked-out target blows up, throws its turret (a tank destroyer has none), chars and
/// smokes; ten seconds later it is revived where it stands. Each <see cref="Sync"/> also notes every target's head point and health for the HUD.
/// </summary>
public partial class TargetRange : Node3D
{
    public const float ReviveSeconds = 10f;
    private const float BlastSize = 1.8f;
    private const float HeadMargin = 0.6f;   // the head point floats this far above the turret's armour box
    private static readonly TankPaint Paint = new(Palette.TargetBody, Palette.Track, Palette.TankHub);

    private readonly List<Target> _targets = new();

    private sealed class Target
    {
        public required int Id;
        public required int Slot;   // index in Simulation.Tanks
        public required TargetSpot Spot;
        public required TankModel Model;
        public required WreckSmoke Smoke;
        public int WreckedTick = -1;
        public Vector3 Head;        // world point over the turret, as of the latest Sync
        public float Health = 1f;   // share left, 0 … 1
        public bool Alive = true;
    }

    public int Count => _targets.Count;

    /// <summary>Adds the targets to the simulation (after the player, so they take the next slots) and builds their models.</summary>
    public void Build(Simulation sim)
    {
        foreach (TargetSpot spot in SandboxMap.TargetSpots)
        {
            int slot = sim.TankCount;
            int id = sim.AddTank(spot.Spec, spot.Position, spot.Yaw);
            var model = new TankModel();
            AddChild(model);
            model.Build(spot.Spec, Paint);
            var smoke = new WreckSmoke();
            model.AddChild(smoke);
            smoke.Build(model.TurretRing);   // pours from the open turret ring (a casemate's roof)
            var target = new Target { Id = id, Slot = slot, Spot = spot, Model = model, Smoke = smoke };
            Observe(target, sim.Tanks[slot], sim.Field.Terrain);   // heads are known before the first Sync
            _targets.Add(target);
        }
    }

    /// <summary>Target <paramref name="index"/>'s head point (where its health bar goes), health left (0 … 1) and whether it is alive.</summary>
    public (Vector3 Head, float Health, bool Alive) Status(int index)
    {
        Target target = _targets[index];
        return (target.Head, target.Health, target.Alive);
    }

    /// <summary>The point over target <paramref name="tankId"/>'s turret, for damage numbers; false for a tank that isn't a target.</summary>
    public bool TryGetHead(int tankId, out Vector3 head)
    {
        Target? target = Find(tankId);
        head = target?.Head ?? Vector3.Zero;
        return target is not null;
    }

    /// <summary>Every target holds still with its gun straight ahead. <paramref name="commands"/> starts at the first target's slot.</summary>
    public void FillInputs(Span<InputCmd> commands, int tick)
    {
        for (int i = 0; i < _targets.Count; i++) commands[i] = new InputCmd(tick, 0, 0, _targets[i].Spot.Yaw, 0f, false);
    }

    /// <summary>Revives wrecks that have lain for <see cref="ReviveSeconds"/>. Call before stepping the simulation.</summary>
    public void ReviveDue(Simulation sim)
    {
        int due = (int)MathF.Round(ReviveSeconds * SimConstants.TickRate);
        foreach (Target t in _targets)
            if (t.WreckedTick >= 0 && sim.Tick - t.WreckedTick >= due)
            {
                sim.Revive(t.Id);
                t.WreckedTick = -1;
            }
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

    /// <summary>Places every target model on its (still) tank, runs its wreck animation and notes its head and health.</summary>
    public void Sync(Simulation sim, float delta)
    {
        foreach (Target target in _targets)
        {
            TankState state = sim.Tanks[target.Slot];
            target.Model.Apply(Observe(target, state, sim.Field.Terrain), state.TurretYaw, state.GunPitch);
            target.Model.Animate(0f, 0f, delta);
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
