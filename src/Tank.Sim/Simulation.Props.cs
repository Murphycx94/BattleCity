using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim;

/// <summary>
/// What shells and tanks do to props (design §4.2, §5.5): shells wear walls down to rubble, knock fences flat and shoot
/// bushes away; tanks knock fences flat, and a fast heavy tank rams brick walls down.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>Ids of the props the tank now driving knocked down this tick; sized so a normal tick never grows it.</summary>
    private readonly List<int> _felled = new(8);

    /// <summary>Reports what <paramref name="tank"/> just drove down (<see cref="_felled"/>), pushed the way it was going.</summary>
    private void ReportFelled(in TankState tank)
    {
        if (_felled.Count == 0) return;
        Vector2 heading = Angles.Forward(tank.Yaw);
        foreach (int id in _felled)
        {
            int index = Field.IndexOf(id);
            Vector2 center = Field.PropAt(index).Footprint.Center;
            // No speed left after the drive (turning on the spot, or stalled against something beyond what it knocked
            // down): pushed away from the hull's centre instead.
            Vector2 push = tank.Speed != 0 ? heading * MathF.Sign(tank.Speed) : Level(new Vector3(center.X - tank.Position.X, 0, center.Y - tank.Position.Y), heading);
            PropDown(index, tank.Id, shellId: 0, ammo: default, new Vector3(center.X, Field.Terrain.HeightAt(center.X, center.Y), center.Y), push);
        }
        _felled.Clear();
    }

    /// <summary>
    /// Puts every destroyed or damaged prop back as it was (sandbox only for now), except a wall or fence with a tank, or a
    /// wreck, on its footprint: that one stays as it is until a later call. Bushes block nothing, so they always come
    /// back. No events: views follow <see cref="Battlefield.IsStanding"/> and <see cref="Battlefield.Durability"/>.
    /// </summary>
    public void RestoreProps()
    {
        for (int i = 0; i < Field.Props.Count; i++)
        {
            ref readonly Prop prop = ref Field.PropAt(i);
            // Full hit points means untouched: a prop that is down always has fewer (0).
            if (!prop.IsDestructible || Field.DurabilityAt(i) == prop.MaxDurability) continue;
            if (prop.IsSolid && UnderATank(prop.Footprint)) continue;
            Field.Restore(i);
        }
    }

    private bool UnderATank(in Obb2 footprint)
    {
        for (int j = 0; j < TankCount; j++)
        {
            ref readonly TankState t = ref _tanks[j];
            if (Obb2.TryGetPenetration(TankMotion.Footprint(t.Spec, t.Position, t.Yaw), footprint, out _)) return true;
        }
        return false;
    }

    /// <summary>
    /// Fences and bushes the shell's path enters before <paramref name="stopT"/> (where it stops this tick, or 1) go down;
    /// the shell flies on. One the path starts inside is left standing: that is where the gun was when it fired (a tank
    /// in a bush doesn't shoot its own cover away), since a shell that flew into one has knocked it down already.
    /// </summary>
    private void KnockThrough(in Shell shell, Vector3 from, Vector3 to, float stopT)
    {
        Vector3 middle = (from + to) * 0.5f;
        float halfLength = Vector3.Distance(from, to) * 0.5f;
        foreach (int i in Field.Flimsy)
        {
            if (!Field.StandingAt(i)) continue;
            ref readonly Prop prop = ref Field.PropAt(i);
            Vector2 center = prop.Footprint.Center;
            if (Vector2.Distance(center, new Vector2(middle.X, middle.Z)) > halfLength + prop.Footprint.HalfExtents.Length()) continue;
            bool crossed = prop.Kind == PropKind.Bush
                ? Intersect.SegmentCylinder(center, prop.Footprint.HalfExtents.X, prop.Bottom, prop.Top, from, to, out float t)
                : Intersect.SegmentPrism(prop.Footprint, prop.Bottom, prop.Top, from, to, out t);
            if (crossed && t > 0f && t <= stopT && Field.Destroy(i))
                PropDown(i, shell.OwnerTankId, shell.Id, shell.Ammo, Vector3.Lerp(from, to, t), Level(to - from, prop.Footprint.AxisForward));
        }
    }

    /// <summary>
    /// A shell strikes wall <paramref name="index"/> at <paramref name="at"/>: it loses <see cref="WallDamage.Direct"/> hit
    /// points (perhaps falling to rubble), then the shell bursts there, splashing everything but this wall.
    /// </summary>
    private void StrikeWall(in Shell shell, int index, Vector3 at, Vector3 direction)
    {
        int damage = WallDamage.Direct(shell.Round, shell.Ammo, shell.Seed);
        HurtWall(shell, index, at, damage, shell.Velocity, Level(direction, Field.PropAt(index).Footprint.AxisForward));
        Explode(shell, at, directSlot: -1, directProp: index);
    }

    /// <summary>
    /// An HE burst at <paramref name="at"/>: every other standing wall within the splash radius takes
    /// <see cref="WallDamage.Splash"/>; every fence and bush within it goes down.
    /// </summary>
    private void SplashProps(in Shell shell, Vector3 at, int directProp)
    {
        float radius = shell.Round.SplashRadius;
        if (radius <= 0) return;
        foreach (int i in Field.Walls)
        {
            if (i == directProp || !Field.StandingAt(i)) continue;
            ref readonly Prop wall = ref Field.PropAt(i);
            int damage = WallDamage.Splash(shell.Round, wall.DistanceTo(at));
            if (damage > 0) HurtWall(shell, i, at, damage, Vector3.Zero, Outward(at, wall));
        }
        foreach (int i in Field.Flimsy)
        {
            if (!Field.StandingAt(i)) continue;
            ref readonly Prop prop = ref Field.PropAt(i);
            if (prop.DistanceTo(at) < radius && Field.Destroy(i)) PropDown(i, shell.OwnerTankId, shell.Id, shell.Ammo, at, Outward(at, prop));
        }
    }

    /// <summary>
    /// Takes <paramref name="damage"/> off a standing wall, reporting it (with the shell's <paramref name="velocity"/> for a
    /// direct hit, zero for a splash), and its fall if this brings it down.
    /// </summary>
    private void HurtWall(in Shell shell, int index, Vector3 at, int damage, Vector3 velocity, Vector2 push)
    {
        int propId = Field.PropAt(index).Id;
        _events.Add(new GameEvent(GameEventType.PropDamaged, Tick, shell.OwnerTankId, shell.Id, at, velocity,
            Damage: damage, Ammo: shell.Ammo, PropId: propId));
        if (Field.Damage(index, damage)) PropDown(index, shell.OwnerTankId, shell.Id, shell.Ammo, at, push);
    }

    private void PropDown(int index, int tankId, int shellId, AmmoType ammo, Vector3 at, Vector2 push) =>
        _events.Add(new GameEvent(GameEventType.PropDestroyed, Tick, tankId, shellId, at, new Vector3(push.X, 0, push.Y),
            Ammo: ammo, PropId: Field.PropAt(index).Id));

    /// <summary>Level unit direction from the burst to the prop's centre (the prop's own forward if the burst is right over it).</summary>
    private static Vector2 Outward(Vector3 burst, in Prop prop) =>
        Level(new Vector3(prop.Footprint.Center.X - burst.X, 0, prop.Footprint.Center.Y - burst.Z), prop.Footprint.AxisForward);

    /// <summary><paramref name="v"/> flattened onto the ground and normalised; <paramref name="fallback"/> if it is (nearly) vertical.</summary>
    private static Vector2 Level(Vector3 v, Vector2 fallback)
    {
        var flat = new Vector2(v.X, v.Z);
        return flat.LengthSquared() > 1e-8f ? Vector2.Normalize(flat) : fallback;
    }
}
