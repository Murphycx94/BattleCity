using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Modes;

/// <summary>The five kinds of supply crate (stage 6 design §4.1). Values go on the wire in 3 bits: append only.</summary>
public enum CrateKind : byte { Repair = 0, Ammo = 1, Armor = 2, Reload = 3, Optics = 4 }

/// <summary>A supply crate on the ground (stage 6 design §4): its id in the match's set (0-based), ground position (x, z), kind.</summary>
public readonly record struct Crate(byte Id, Vector2 Position, CrateKind Kind);

/// <summary>
/// What one pickup did: the kind; <see cref="Amount"/> = health restored (repair), AP rounds added (ammo) or 1 (a layer added),
/// 0 when it did nothing; <see cref="Amount2"/> = HE rounds added (ammo only); <see cref="Layers"/> = the tank's layers of that
/// kind afterwards (0 for repair, ammo).
/// </summary>
public readonly record struct CrateTake(CrateKind Kind, int Amount, int Amount2, byte Layers)
{
    /// <summary>Full health, full racks or three layers already: the crate is gone all the same.</summary>
    public bool Wasted => Amount == 0 && Amount2 == 0;
}

/// <summary>One crate eaten this tick: by which tank, which crate, and what it did.</summary>
public readonly record struct CratePickup(int TankId, byte CrateId, CrateTake Take);

/// <summary>The crate numbers (stage 6 design §4.2) and what each kind does for a tank.</summary>
public static class CrateRules
{
    /// <summary>A ulong mask per viewer; the riverland places <c>CratePlacer.Count</c> = 48.</summary>
    public const int MaxCrates = 64;
    public const int Kinds = 5;

    /// <summary>Half of the 1.2 m square; the hull's OBB touching this circle eats it.</summary>
    public const float HalfSize = 0.6f;

    /// <summary>The box's height.</summary>
    public const float Height = 1.0f;

    /// <summary>The sign's top above the ground: what sight is tested against.</summary>
    public const float SignTop = 1.8f;

    public const int MaxLayers = 3;
    public const float RepairShare = 0.30f, AmmoShare = 0.5f, ArmorPerLayer = 0.10f, ReloadPerLayer = 0.08f, OpticsPerLayer = 0.10f;

    /// <summary>600 → 180, 900 → 270, 1400 → 420, 950 → 285.</summary>
    public static int RepairAmount(int maxHealth) => CeilingShare(maxHealth, RepairShare);

    /// <summary>40 → 20, 15 → 8, 25 → 13, 30 → 15, 12 → 6.</summary>
    public static int AmmoAmount(int capacity) => CeilingShare(capacity, AmmoShare);

    /// <summary>
    /// ⌈<paramref name="whole"/> × <paramref name="share"/>⌉, the share taken as whole thousandths and the product worked out in
    /// integers: in float 1400 × 0.3f lands a hair above 420 and would round up to 421.
    /// </summary>
    private static int CeilingShare(int whole, float share)
    {
        int permille = (int)MathF.Round(share * 1000f);
        return (whole * permille + 999) / 1000;
    }

    /// <summary>1, 0.9, 0.8, 0.7.</summary>
    public static float ArmorFactor(int layers) => 1f - ArmorPerLayer * layers;

    /// <summary>Shell damage after the armour layers: round half up, never below 1 for a hit that did damage (damage ≤ 0 stays as it is).</summary>
    public static int ScaleShellDamage(int damage, int layers) =>
        damage <= 0 ? damage : Math.Max(1, Penetration.RoundHalfUp(damage * ArmorFactor(layers)));

    /// <summary>4 s → 3.68, 3.36, 3.04.</summary>
    public static float ReloadTime(TankSpec spec, int layers) => spec.ReloadTime * (1f - ReloadPerLayer * layers);

    /// <summary>270 → 297, 324, 351.</summary>
    public static float ViewRange(in TankState tank) => tank.Spec.ViewRange * (1f + OpticsPerLayer * tank.OpticsLayers);

    /// <summary>The tank's layers of <paramref name="kind"/>: armour, reload or optics; 0 for repair and ammo.</summary>
    public static byte Layers(in TankState tank, CrateKind kind) => kind switch
    {
        CrateKind.Armor => tank.ArmorLayers,
        CrateKind.Reload => tank.ReloadLayers,
        CrateKind.Optics => tank.OpticsLayers,
        _ => 0,
    };

    /// <summary>Whether the crate would do anything for the tank right now (design §4.5: bots skip crates that do nothing).</summary>
    public static bool IsUseful(in TankState tank, CrateKind kind) => kind switch
    {
        CrateKind.Repair => tank.Health < tank.Spec.MaxHealth,
        CrateKind.Ammo => tank.ApLeft < tank.Spec.Ap.Capacity || tank.HeLeft < tank.Spec.He.Capacity,
        _ => Layers(tank, kind) < MaxLayers,
    };

    /// <summary>"repair", "ammo", "armor", "reload", "optics" (logs, tests).</summary>
    public static string Id(CrateKind kind) => kind switch
    {
        CrateKind.Repair => "repair",
        CrateKind.Ammo => "ammo",
        CrateKind.Armor => "armor",
        CrateKind.Reload => "reload",
        CrateKind.Optics => "optics",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown crate kind."),
    };
}

/// <summary>
/// The crates of one match. Built once at the start (allocates), then allocation-free. Only the server and the simulation
/// tests hold one; clients know crates only through <c>Tank.Net</c>'s <c>CrateKnowledge</c>. Crates aren't props (design
/// §4.1): nothing blocks on them, shells fly through them, rays don't see them; this is a table on the rules' side.
/// </summary>
public sealed class CrateSet
{
    private readonly Crate[] _crates;

    /// <summary>Per crate, the id of the tank that ate it, 0 while it stands.</summary>
    private readonly int[] _takenBy;

    private readonly CratePickup[] _pickups = new CratePickup[CrateRules.MaxCrates];
    private int _pickupCount;

    /// <summary>Bit <c>id</c> set while the crate stands.</summary>
    private ulong _standing;

    /// <param name="crates">Ids must be 0 … n−1 in order, n ≤ <see cref="CrateRules.MaxCrates"/>; ArgumentException otherwise.</param>
    public CrateSet(IReadOnlyList<Crate> crates)
    {
        ArgumentNullException.ThrowIfNull(crates);
        if (crates.Count > CrateRules.MaxCrates)
            throw new ArgumentException($"At most {CrateRules.MaxCrates} crates, not {crates.Count}.", nameof(crates));
        _crates = new Crate[crates.Count];
        for (int i = 0; i < _crates.Length; i++)
        {
            if (crates[i].Id != i) throw new ArgumentException($"Crate {i} has id {crates[i].Id}: ids run 0 … n−1 in order.", nameof(crates));
            _crates[i] = crates[i];
        }
        _takenBy = new int[_crates.Length];
        _standing = _crates.Length == CrateRules.MaxCrates ? ulong.MaxValue : (1ul << _crates.Length) - 1;
    }

    public int Count => _crates.Length;

    /// <summary>The crate with this id.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not 0 … <see cref="Count"/> − 1.</exception>
    public Crate this[int id] => _crates[Index(id)];

    public ReadOnlySpan<Crate> All => _crates;

    /// <summary>Whether the crate still stands; false for an id outside 0 … <see cref="Count"/> − 1 (no such crate stands).</summary>
    public bool IsStanding(int id) => (uint)id < (uint)_crates.Length && (_standing >> id & 1ul) != 0;

    /// <summary>The tank id that ate it, 0 while it stands.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not 0 … <see cref="Count"/> − 1.</exception>
    public int TakenBy(int id) => _takenBy[Index(id)];

    /// <summary>
    /// <paramref name="id"/> once it is known to be a crate's. Without the check an id of 64 would read crate 0's bit (C# masks
    /// a ulong's shift count to 6 bits) and fall off the arrays.
    /// </summary>
    private int Index(int id) =>
        (uint)id < (uint)_crates.Length ? id : throw new ArgumentOutOfRangeException(nameof(id), id, $"Crate ids run 0 … {_crates.Length - 1}.");

    public int StandingCount => BitOperations.PopCount(_standing);

    /// <summary>Bit <c>id</c> set while the crate stands.</summary>
    public ulong StandingMask => _standing;

    /// <summary>Bumps on every <see cref="Take"/> (the bots and tests watch it).</summary>
    public int Version { get; private set; }

    /// <summary>This tick's pickups (since the last <see cref="Collect"/>), in the order eaten: the server turns the human ones into Crate messages.</summary>
    public ReadOnlySpan<CratePickup> Pickups => _pickups.AsSpan(0, _pickupCount);

    /// <summary>Marks the crate eaten by <paramref name="tankId"/>; false, and nothing changed, if it was gone already or no crate has that id.</summary>
    public bool Take(int id, int tankId)
    {
        if (!IsStanding(id)) return false;
        _standing &= ~(1ul << id);
        _takenBy[id] = tankId;
        Version++;
        return true;
    }

    /// <summary>
    /// One tick's pickups (design §4.2): tanks in slot order (ids only grow, so that is id order — the lower id wins a crate two
    /// hulls touch in the same tick), live ones only, whose hull OBB (<see cref="TankMotion.Footprint"/>) intersects the crate's
    /// circle (<see cref="CrateRules.HalfSize"/>); each eaten crate goes through <see cref="Simulation.TakeCrate"/>. Allocation-free.
    /// </summary>
    public void Collect(Simulation sim)
    {
        ArgumentNullException.ThrowIfNull(sim);
        _pickupCount = 0;
        if (_standing == 0) return;
        ReadOnlySpan<TankState> tanks = sim.Tanks;   // TakeCrate changes a tank's state, never the order
        for (int i = 0; i < tanks.Length; i++)
        {
            ref readonly TankState t = ref tanks[i];
            if (t.IsDestroyed) continue;
            Obb2 hull = TankMotion.Footprint(t.Spec, t.Position, t.Yaw);
            float reach = CrateRules.HalfSize + hull.HalfExtents.Length();   // the circle plus the hull's half-diagonal
            float reachSquared = reach * reach;
            for (ulong rest = _standing; rest != 0; rest &= rest - 1)
            {
                int id = BitOperations.TrailingZeroCount(rest);
                ref readonly Crate crate = ref _crates[id];
                if (Vector2.DistanceSquared(t.Position, crate.Position) > reachSquared) continue;
                if (!hull.IntersectsCircle(crate.Position, CrateRules.HalfSize)) continue;
                Take(id, t.Id);
                CrateTake take = sim.TakeCrate(t.Id, crate);
                _pickups[_pickupCount++] = new CratePickup(t.Id, crate.Id, take);
            }
        }
    }

    /// <summary>
    /// Which crates <paramref name="observer"/> sees right now (design §4.4): bit <c>id</c> set for each crate within
    /// <see cref="CrateRules.ViewRange"/> that <see cref="CrateSight.Sees"/> from the observer, eaten ones included (seeing an
    /// empty spot is how a client learns a crate is gone); 0 for a wreck. Allocation-free: the server asks for every human
    /// seat at each vision update.
    /// </summary>
    public ulong VisibleTo(in TankState observer, Battlefield field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (observer.IsDestroyed || _crates.Length == 0) return 0;
        float view = CrateRules.ViewRange(observer);
        float viewSquared = view * view;
        Vector3 eye = Spotting.TurretTop(observer, TankPose.Compute(observer, field.Terrain));   // once, not once a crate
        ulong mask = 0;
        for (int id = 0; id < _crates.Length; id++)
        {
            if (Vector2.DistanceSquared(observer.Position, _crates[id].Position) > viewSquared) continue;
            if (CrateSight.LineClear(eye, _crates[id].Position, field)) mask |= 1ul << id;
        }
        return mask;
    }
}
