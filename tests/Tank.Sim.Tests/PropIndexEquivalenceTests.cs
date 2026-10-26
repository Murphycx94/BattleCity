using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>
/// The prop index changes nothing but speed (riverland design §4.1): a whole battle comes out the same, tick by tick,
/// with the index on and with every query scanning the full lists.
/// </summary>
public sealed class PropIndexEquivalenceTests
{
    private const int Tanks = 8;

    [Fact]
    public void Sandbox_battle_is_identical_with_and_without_the_index()
    {
        var spawns = SandboxMap.Spawns.Solo.Take(Tanks).Select(s => (s.Position, s.Yaw)).ToArray();
        var (shots, propsDown, _, _, _) = RunTwice(SandboxMap.Create, spawns, ticks: 1800);
        Assert.True(shots > 50 && propsDown > 20, $"a quiet battle: {shots} shots, {propsDown} props down");
    }

    [Fact]
    public void Dense_field_battle_is_identical_with_and_without_the_index()
    {
        var spawns = RoundTheMiddle();
        var (shots, propsDown, wallsHurt, _, _) = RunTwice(
            () => ScatteredField.Build(seed: 29, rocks: 400, bushes: 1500, walls: 40, fences: 20, minSegments: 3, maxSegments: 10),
            spawns, ticks: 1200);
        Assert.True(shots > 50 && propsDown > 20 && wallsHurt > 3, $"a quiet battle: {shots} shots, {propsDown} props down, {wallsHurt} walls hurt");
    }

    [Fact]
    public void Synthetic_forest_battle_is_identical_with_and_without_the_index()
    {
        // 2400 trees about 6 m apart over the whole 300 m field (as dense as a riverland forest), 200 bushes, 100 rocks.
        var spawns = RoundTheMiddle();
        var (shots, propsDown, _, _, _) = RunTwice(() => ScatteredField.Forest(seed: 31, trees: 2400, bushes: 200, rocks: 100), spawns, ticks: 1200);
        Assert.True(shots > 50 && propsDown > 50, $"a quiet battle: {shots} shots, {propsDown} props down");
    }

    [Fact]
    public void Houses_among_trees_battle_is_identical_with_and_without_the_index()
    {
        // 900 trees about 10 m apart, 100 bushes, 50 rocks and 14 houses round the middle, where the tanks drive, ram and shoot.
        var spawns = RoundTheMiddle();
        var (shots, propsDown, _, housesHurt, housesRammed) =
            RunTwice(() => ScatteredField.Forest(seed: 37, trees: 900, bushes: 100, rocks: 50, houses: 14), spawns, ticks: 1500);
        Assert.True(shots > 50 && propsDown > 30 && housesHurt > 5 && housesRammed > 0,
            $"a quiet battle: {shots} shots, {propsDown} props down, {housesHurt} hits on houses, {housesRammed} houses rammed down");
    }

    /// <summary>The tanks on a ring 70 m round the middle of a <see cref="ScatteredField"/>, facing it.</summary>
    private static (Vector2 Position, float Yaw)[] RoundTheMiddle()
    {
        var middle = new Vector2(ScatteredField.Size / 2, ScatteredField.Size / 2);
        return Enumerable.Range(0, Tanks).Select(k =>
        {
            Vector2 at = middle + new Vector2(MathF.Cos(k * Angles.TwoPi / Tanks), MathF.Sin(k * Angles.TwoPi / Tanks)) * 70f;
            return (at, Angles.YawOf(middle - at));
        }).ToArray();
    }

    /// <summary>
    /// Runs the same scripted battle on two copies of <paramref name="makeField"/>, one with the index and one without,
    /// and compares after every tick: every tank, the events, the shell count, every prop's hit points and who sees whom.
    /// Tank k is the k-th of the four kinds (twice round), on team k, at <paramref name="spawns"/>[k].
    /// </summary>
    /// <returns>What went on, to show the battle was busy: shots fired, props knocked down, hits on walls and houses, hits on houses alone, houses rammed down.</returns>
    internal static (int Shots, int PropsDown, int WallsHurt, int HousesHurt, int HousesRammed) RunTwice(Func<Battlefield> makeField, IReadOnlyList<(Vector2 Position, float Yaw)> spawns, int ticks)
    {
        Battlefield indexed = makeField(), scanned = makeField();
        scanned.UseIndex = false;
        Simulation a = new(indexed), b = new(scanned);
        for (int k = 0; k < Tanks; k++)
        {
            TankSpec spec = TankCatalog.All[k % TankCatalog.All.Count];
            a.AddTank(spec, spawns[k].Position, spawns[k].Yaw, (byte)k);
            b.AddTank(spec, spawns[k].Position, spawns[k].Yaw, (byte)k);
        }
        VisionTracker seenA = new(a), seenB = new(b);
        var script = new Script(seed: 5, spawns);
        var inputs = new InputCmd[Tanks];
        int shots = 0, propsDown = 0, propsHurt = 0, housesHurt = 0, housesRammed = 0;
        for (int tick = 1; tick <= ticks; tick++)
        {
            script.Fill(a, inputs);
            a.Step(inputs);
            b.Step(inputs);
            seenA.Update();
            seenB.Update();
            AssertSame(a, b, seenA, seenB, tick);
            foreach (GameEvent e in a.Events)
            {
                if (e.Type == GameEventType.ShellFired) shots++;
                if (e.Type == GameEventType.PropDestroyed) propsDown++;
                if (e.Type == GameEventType.PropDamaged) propsHurt++;
                bool house = (e.Type is GameEventType.PropDamaged or GameEventType.PropDestroyed) && a.Field.PropById(e.PropId).Kind == PropKind.House;
                if (house && e.Type == GameEventType.PropDamaged) housesHurt++;
                if (house && e.Type == GameEventType.PropDestroyed && e.ShellId == 0) housesRammed++;
            }
        }
        return (shots, propsDown, propsHurt, housesHurt, housesRammed);
    }

    private static void AssertSame(Simulation a, Simulation b, VisionTracker seenA, VisionTracker seenB, int tick)
    {
        Assert.Equal(a.TankCount, b.TankCount);
        for (int k = 0; k < a.TankCount; k++)
            if (!a.Tanks[k].Equals(b.Tanks[k])) Assert.Fail($"tick {tick}: tank {k} differs");
        if (!a.Events.SequenceEqual(b.Events)) Assert.Fail($"tick {tick}: events differ");
        Assert.Equal(a.ShellCount, b.ShellCount);
        for (int i = 0; i < a.Field.Props.Count; i++)
            if (a.Field.DurabilityAt(i) != b.Field.DurabilityAt(i)) Assert.Fail($"tick {tick}: prop {a.Field.Props[i].Id} hit points differ");
        foreach (TankState t in a.Tanks)
            for (byte team = 0; team < Tanks; team++)
                if (seenA.IsVisible(t.Id, team) != seenB.IsVisible(t.Id, team)) Assert.Fail($"tick {tick}: tank {t.Id} seen by team {team} differs");
    }

    /// <summary>
    /// The drivers: every 45 ticks each picks a new throttle and steering at random; the aim sweeps round at a steady
    /// rate, its pitch a little up or down; each fires as soon as it is loaded and switches round every third shot.
    /// </summary>
    private sealed class Script
    {
        private const int Hold = 45;
        private const float Sweep = 0.02f;   // rad per tick
        private XorShift _rng;
        private readonly sbyte[] _throttle = new sbyte[Tanks], _steer = new sbyte[Tanks];
        private readonly float[] _aimYaw = new float[Tanks], _aimPitch = new float[Tanks];

        public Script(uint seed, IReadOnlyList<(Vector2 Position, float Yaw)> spawns)
        {
            _rng = new XorShift(seed);
            for (int k = 0; k < Tanks; k++) _aimYaw[k] = spawns[k].Yaw;
        }

        public void Fill(Simulation sim, InputCmd[] inputs)
        {
            int tick = sim.Tick + 1;
            for (int k = 0; k < Tanks; k++)
            {
                if (tick % Hold == 1)
                {
                    _throttle[k] = (sbyte)((int)(_rng.NextUInt() % 3) - 1);
                    _steer[k] = (sbyte)((int)(_rng.NextUInt() % 3) - 1);
                    _aimPitch[k] = -0.05f + _rng.NextFloat() * 0.1f;
                }
                _aimYaw[k] = Angles.Wrap(_aimYaw[k] + (k % 2 == 0 ? Sweep : -Sweep));
                TankState t = sim.Tanks[k];
                AmmoType ammo = t.ShotCount / 3 % 2 == 0 ? AmmoType.AP : AmmoType.HE;
                inputs[k] = new InputCmd(tick, _throttle[k], _steer[k], _aimYaw[k], _aimPitch[k], Fire: t.Reload <= 0, ammo);
            }
        }
    }
}
