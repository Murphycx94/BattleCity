using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim;

/// <summary>Shell flight and everything a shell can do when it lands: strike armour, burst, splash, wreck a tank.</summary>
public sealed partial class Simulation
{
    private readonly OrientedBox[] _hullBoxes = new OrientedBox[MaxTanks];
    private readonly OrientedBox[] _turretBoxes = new OrientedBox[MaxTanks];

    /// <summary>
    /// Flies every shell one tick. Its path this tick stops at whichever comes first: the ground or a rock, or a tank's
    /// hull or turret box (never the shooter's own). Shells that leave the map or time out vanish without an event.
    /// </summary>
    private void StepShells()
    {
        if (ShellCount == 0) return;
        SnapshotHitBoxes();
        for (int i = ShellCount - 1; i >= 0; i--)
        {
            ref Shell shell = ref _shells[i];
            Vector3 from = shell.Position;
            ShellStep step = Ballistics.Step(ref shell, Field, out RayHit rayHit);
            float groundT = step == ShellStep.Hit ? rayHit.T : 1f;
            if (FindTankHit(shell.OwnerTankId, from, shell.Position, groundT, out int slot, out ArmorHit hit))
                StrikeTank(shell, slot, hit, shell.Position - from);
            else if (step == ShellStep.Hit)
                Explode(shell, rayHit.Point, directSlot: -1);
            else if (step == ShellStep.Flying)
                continue;
            _shells[i] = _shells[--ShellCount];
        }
    }

    /// <summary>Tanks don't move while shells fly, so their boxes are worked out once per tick.</summary>
    private void SnapshotHitBoxes()
    {
        for (int j = 0; j < TankCount; j++)
        {
            ref readonly TankState t = ref _tanks[j];
            TankPose pose = TankPose.Compute(t, Field.Terrain);
            _hullBoxes[j] = pose.HullBox(t.Spec);
            _turretBoxes[j] = pose.TurretBox(t.Spec, t.TurretYaw);
        }
    }

    /// <summary>The nearest armour box the segment enters before <paramref name="maxT"/>, skipping the shooter.</summary>
    private bool FindTankHit(int ownerId, Vector3 from, Vector3 to, float maxT, out int slot, out ArmorHit hit)
    {
        slot = -1;
        hit = default;
        Vector3 middle = (from + to) * 0.5f;
        float halfLength = Vector3.Distance(from, to) * 0.5f;
        for (int j = 0; j < TankCount; j++)
        {
            if (_tanks[j].Id == ownerId) continue;
            ref readonly OrientedBox hull = ref _hullBoxes[j];
            ref readonly OrientedBox turret = ref _turretBoxes[j];
            float reach = MathF.Max(hull.BoundingRadius, Vector3.Distance(hull.Center, turret.Center) + turret.BoundingRadius);
            if (Vector3.Distance(hull.Center, middle) > halfLength + reach) continue;   // nowhere near this tank
            TankSpec spec = _tanks[j].Spec;
            if (!ArmorHit.Find(spec.HullArmor, spec.TurretArmor, hull, turret, from, to, maxT, out ArmorHit candidate)) continue;
            (slot, hit, maxT) = (j, candidate, candidate.T);
        }
        return slot >= 0;
    }

    /// <summary>
    /// A shell meets a tank's armour. A live tank takes the rolled result (AP may bounce; HE never does, and bursts on the
    /// plate). A wreck just stops the shell: AP is spent, HE still bursts and splashes the tanks around it.
    /// </summary>
    private void StrikeTank(in Shell shell, int slot, in ArmorHit hit, Vector3 direction)
    {
        ref TankState target = ref _tanks[slot];
        if (target.IsDestroyed)
        {
            Explode(shell, hit.Point, slot);
            return;
        }
        float angle = Penetration.ImpactAngle(direction, hit.Normal);
        Impact impact = shell.Ammo == AmmoType.HE
            ? Penetration.HeDirect(shell.Round, hit.Thickness, angle, shell.Seed)
            : Penetration.Ap(shell.Round, shell.Caliber, hit.Thickness, angle, shell.Seed);
        _events.Add(new GameEvent(GameEventType.TankHit, Tick, shell.OwnerTankId, shell.Id, hit.Point, hit.Normal,
            target.Id, impact.Result, impact.Damage, shell.Ammo));
        Damage(ref target, impact.Damage, shell.OwnerTankId, shell.Id);
        if (shell.Ammo == AmmoType.HE) Explode(shell, hit.Point, slot);
    }

    /// <summary>
    /// The shell bursts at <paramref name="at"/>. A round with a splash radius (HE) hurts every other live tank in range,
    /// except the shooter and <paramref name="directSlot"/> (the tank it struck directly, already dealt with).
    /// </summary>
    private void Explode(in Shell shell, Vector3 at, int directSlot)
    {
        _events.Add(new GameEvent(GameEventType.ShellExploded, Tick, shell.OwnerTankId, shell.Id, at, Vector3.Zero, Ammo: shell.Ammo));
        if (shell.Round.SplashRadius <= 0) return;
        for (int j = 0; j < TankCount; j++)
        {
            ref TankState target = ref _tanks[j];
            if (j == directSlot || target.Id == shell.OwnerTankId || target.IsDestroyed) continue;
            float distance = TankPose.Compute(target, Field.Terrain).HullBox(target.Spec).DistanceTo(at);
            int damage = Penetration.Splash(shell.Round, distance, target.Spec.HullArmor.Armor.Thinnest);
            if (damage <= 0) continue;
            _events.Add(new GameEvent(GameEventType.SplashDamage, Tick, shell.OwnerTankId, shell.Id, at, Vector3.Zero,
                target.Id, HitResult.None, damage, shell.Ammo));
            Damage(ref target, damage, shell.OwnerTankId, shell.Id);
        }
    }

    /// <summary>Takes <paramref name="damage"/> off a live tank. At zero it becomes a wreck and stops where it is.</summary>
    private void Damage(ref TankState target, int damage, int attackerId, int shellId)
    {
        if (damage <= 0 || target.IsDestroyed) return;
        target.Health -= damage;
        if (target.Health > 0) return;
        target.Health = 0;
        target.IsDestroyed = true;
        target.Speed = 0f;
        Vector3 wreck = TankPose.Compute(target, Field.Terrain).Position;
        _events.Add(new GameEvent(GameEventType.TankDestroyed, Tick, attackerId, shellId, wreck, Vector3.Zero, TargetTankId: target.Id));
    }
}
