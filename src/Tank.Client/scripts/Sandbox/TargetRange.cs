using Godot;
using Tank.Client.Art;
using Tank.Client.Combat;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Client.Sandbox;

/// <summary>
/// The six practice targets on <see cref="SandboxMap.TargetSpots"/>: blue-grey medium tanks that sit still with their
/// guns straight ahead. A knocked-out target blows up, throws its turret, chars and smokes; ten seconds later it is
/// revived where it stands.
/// </summary>
public partial class TargetRange : Node3D
{
    public const float ReviveSeconds = 10f;
    private const float BlastSize = 1.8f;
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
    }

    public int Count => _targets.Count;

    /// <summary>Adds the targets to the simulation (after the player, so they take the next slots) and builds their models.</summary>
    public void Build(Simulation sim)
    {
        foreach (TargetSpot spot in SandboxMap.TargetSpots)
        {
            int slot = sim.TankCount;
            int id = sim.AddTank(TankCatalog.Medium, spot.Position, spot.Yaw);
            var model = new TankModel();
            AddChild(model);
            model.Build(TankCatalog.Medium, Paint);
            var smoke = new WreckSmoke();
            model.AddChild(smoke);
            smoke.Build(model.TurretRing);   // pours from the open turret ring
            _targets.Add(new Target { Id = id, Slot = slot, Spot = spot, Model = model, Smoke = smoke });
        }
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
            fx.Explosion(e.Position.ToGodot() + Vector3.Up * 1.2f, onRock: false, BlastSize);
        }
        else if (e.Type == GameEventType.TankRevived)
        {
            target.Model.Restore();
            target.Smoke.Emitting = false;
        }
    }

    /// <summary>Places every target model on its (still) tank and runs its wreck animation.</summary>
    public void Sync(Simulation sim, float delta)
    {
        foreach (Target target in _targets)
        {
            TankState state = sim.Tanks[target.Slot];
            target.Model.Apply(TankPose.Compute(state, sim.Field.Terrain), state.TurretYaw, state.GunPitch);
            target.Model.Animate(0f, 0f, delta);
        }
    }

    private Target? Find(int tankId)
    {
        foreach (Target t in _targets)
            if (t.Id == tankId) return t;
        return null;
    }
}
