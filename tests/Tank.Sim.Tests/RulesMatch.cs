using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// A match's rules on a flat 200 m field with close-set spawns, so kills come quickly: the simulation, the rules, and
/// standing orders per tank (stage 4 design §2). Shared by the rules tests.
/// </summary>
internal sealed class RulesMatch
{
    /// <summary>Team 0 along z = 150 facing north, team 1 along z = 100 facing south, 50 m apart; solo spawns in a row.</summary>
    public static readonly SpawnLayout Layout = new(
        Team: Enumerable.Range(0, 5).Select(i => new SpawnPoint(new Vector2(60 + 20 * i, 150), 0f, 0))
            .Concat(Enumerable.Range(0, 5).Select(i => new SpawnPoint(new Vector2(60 + 20 * i, 100), MathF.PI, 1))).ToArray(),
        Solo: Enumerable.Range(0, 10).Select(i => new SpawnPoint(new Vector2(20 + 18 * i, 100 + (i % 2) * 50), (i % 2) * MathF.PI, 0)).ToArray());

    /// <summary>Each 30 m behind its team's spawn line (a standard match needs them; the other modes ignore them).</summary>
    public static readonly TeamBase[] Bases =
    {
        new(0, new Vector2(100, 180), TeamBase.StandardRadius),
        new(1, new Vector2(100, 70), TeamBase.StandardRadius),
    };

    public readonly Simulation Sim = new(TestWorld.FlatField());
    public readonly MatchRules Rules;
    private readonly Dictionary<int, InputCmd> _orders = new();

    public RulesMatch(BattleMode mode, uint seed = 0, IReadOnlyList<ZoneRing>? zone = null, IReadOnlyList<Crate>? crates = null) =>
        Rules = new MatchRules(mode, Sim, Layout, bases: Bases, seed: seed, zone: zone, crates: crates);

    public int Join(byte slot, byte team = 0, TankSpec? spec = null) => Rules.Join(slot, spec ?? TankCatalog.Medium, team);

    /// <summary>What a tank does every tick until told otherwise (default: hold still, gun ahead).</summary>
    public void Order(int tankId, InputCmd cmd) => _orders[tankId] = cmd;

    public List<GameEvent> Run(int ticks)
    {
        var events = new List<GameEvent>();
        var inputs = new InputCmd[Simulation.MaxTanks].AsSpan();
        for (int i = 0; i < ticks; i++)
        {
            inputs = inputs[..Sim.TankCount];
            for (int k = 0; k < inputs.Length; k++)
            {
                TankState t = Sim.Tanks[k];
                inputs[k] = _orders.TryGetValue(t.Id, out InputCmd cmd) ? cmd : new InputCmd(0, 0, 0, t.Yaw, 0f, false);
            }
            Sim.Step(inputs);
            Rules.Update();
            events.AddRange(Sim.Events);
        }
        return events;
    }

    /// <summary>Lays <paramref name="shooter"/>'s gun on <paramref name="target"/>'s hull front and keeps firing <paramref name="ammo"/>.</summary>
    public void Attack(int shooter, int target, AmmoType ammo = AmmoType.AP)
    {
        TankState s = Sim.Tanks[Sim.SlotOf(shooter)], t = Sim.Tanks[Sim.SlotOf(target)];
        var point = new Vector3(t.Position.X, 1.02f, t.Position.Y) + Vector3.Transform(new Vector3(0, 0, -3.1f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, t.Yaw));
        var (yaw, pitch) = Angles.AimAngles(point - TankPose.Compute(s, Sim.Field.Terrain).Trunnion(s.Spec));
        Order(shooter, new InputCmd(0, 0, 0, yaw, pitch, true, ammo));
    }

    public TankState Tank(int id) => Sim.Tanks[Sim.SlotOf(id)];

    public void Weaken(int id, int health) => Sim.TankRef(id).Health = health;

    /// <summary>Puts <paramref name="id"/> at (<paramref name="x"/>, <paramref name="z"/>) facing <paramref name="yaw"/>.</summary>
    public void Place(int id, float x, float z, float yaw = 0f)
    {
        Sim.TankRef(id).Position = new Vector2(x, z);
        Sim.TankRef(id).Yaw = yaw;
    }

    /// <summary><paramref name="shooter"/> at (100, 150) facing north; the two others 40 m in front of it, 40 m apart, facing it.</summary>
    public void LineUp(int shooter, int first, int second)
    {
        Place(shooter, 100, 150, 0f);
        Place(first, 100, 110, MathF.PI);
        Place(second, 140, 110, MathF.PI);
    }
}
