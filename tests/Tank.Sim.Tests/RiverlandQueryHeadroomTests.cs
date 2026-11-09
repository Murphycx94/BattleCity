using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;
using Xunit.Abstractions;

namespace Tank.Sim.Tests;

/// <summary>
/// The prop index's query buffers have room to spare on the riverland's densest ground (riverland design §4.1): a query
/// whose candidates outgrow its buffer silently falls back to scanning every prop, which is correct but slow. A medium tank
/// drives through the east forest, down the town's streets and along the rocks at the plateau's foot and up its ramp, firing ahead; every
/// tick the test asks the index the questions the simulation asks there and keeps, per kind of query, the most entries
/// its buffer had to hold: the candidates as gathered cell by cell, before duplicates (a prop in several cells) go.
/// </summary>
public sealed class RiverlandQueryHeadroomTests
{
    /// <summary>TankMotion's ResolveSlack (private): Resolve asks for the blockers this far beyond the hull's reach.</summary>
    private const float ResolveSlack = 4f;

    /// <summary>Battlefield's RayBufferSize (private): Raycast's buffer.</summary>
    private const int RayBufferSize = 1024;

    private const int FireEvery = SimConstants.TickRate;
    private const float ReachedPoint = 6f;
    private const int MaxTicks = 120 * SimConstants.TickRate;

    private static readonly (string Name, Vector2[] Path)[] Drives =
    {
        ("east forest", new Vector2[] { new(880, 470), new(990, 500), new(1120, 480), new(1120, 560) }),
        ("town streets", new Vector2[] { new(600, 555), new(700, 560), new(760, 575), new(778, 490) }),
        // Along the rocks at the foot of the plateau's south cliff, round to the south ramp's foot and up between its rocks.
        ("plateau's foot", new Vector2[]
        {
            new(91, 339), new(187, 339), new(201, 341), new(210, 365), new(212, 420), new(220, 426), new(240, 424),
            new(236, 370), new(233, 330), new(230, 296),
        }),
    };

    private readonly ITestOutputHelper _output;

    public RiverlandQueryHeadroomTests(ITestOutputHelper output) => _output = output;

    private enum Query { Resolve, Spotting, KnockThrough, Raycast }

    private delegate ReadOnlySpan<int> Ask(Span<int> buffer);

    [Fact]
    public void Query_candidates_stay_under_half_the_buffer()
    {
        var most = new int[4];
        foreach ((string name, Vector2[] path) in Drives)
        {
            var here = new int[4];
            int ticks = Drive(path, here);
            _output.WriteLine($"{name}: {ticks} ticks; most gathered: resolve {here[0]}, spotting {here[1]}, knock-through {here[2]}, raycast {here[3]}");
            for (int q = 0; q < most.Length; q++) most[q] = Math.Max(most[q], here[q]);
        }
        Assert.True(most[(int)Query.Resolve] < Battlefield.QueryBufferSize / 2, $"resolve: {most[(int)Query.Resolve]} blockers");
        Assert.True(most[(int)Query.Spotting] < Battlefield.QueryBufferSize / 2, $"spotting: {most[(int)Query.Spotting]} bushes and trees");
        Assert.True(most[(int)Query.KnockThrough] < Battlefield.QueryBufferSize / 2, $"knock-through: {most[(int)Query.KnockThrough]} flimsy props");
        Assert.True(most[(int)Query.Raycast] < RayBufferSize / 2, $"raycast: {most[(int)Query.Raycast]} shell stoppers");
    }

    /// <summary>Drives <paramref name="path"/> to its end, noting the most candidates per query in <paramref name="most"/>; returns the ticks it took.</summary>
    private static int Drive(Vector2[] path, int[] most)
    {
        Battlefield field = RiverlandFixture.Field.Fresh();
        var sim = new Simulation(field);
        sim.AddTank(TankCatalog.Medium, path[0], Angles.YawOf(path[1] - path[0]));
        var buffer = new int[8 * field.Props.Count];   // room for every prop in several cells
        var before = new Dictionary<int, Vector3>();
        int next = 1;
        while (sim.Tick < MaxTicks)
        {
            TankState tank = sim.Tanks[0];
            if (Vector2.Distance(tank.Position, path[next]) <= ReachedPoint && ++next == path.Length) return sim.Tick;
            var (throttle, steer) = Steering.Toward(tank, path[next], stopThere: false);
            bool fire = sim.Tick % FireEvery == 0;
            before.Clear();
            foreach (Shell s in sim.Shells) before[s.Id] = s.Position;
            sim.Step(new[] { new InputCmd(sim.Tick + 1, throttle, steer, tank.Yaw, 0f, fire) });

            tank = sim.Tanks[0];
            Obb2 hull = TankMotion.Footprint(tank.Spec, tank.Position, tank.Yaw);
            Vector2 at = tank.Position;
            Note(most, Query.Resolve, field, PropSet.Blockers, buffer, b => field.Near(PropSet.Blockers, hull.Center, hull.HalfExtents.Length() + ResolveSlack, b));
            Note(most, Query.Spotting, field, PropSet.Foliage, buffer, b => field.Near(PropSet.Foliage, at, Spotting.BushReach, b));
            foreach (Shell s in sim.Shells)
            {
                if (!before.TryGetValue(s.Id, out Vector3 from)) continue;
                Vector2 a = new(from.X, from.Z), z = new(s.Position.X, s.Position.Z);
                Note(most, Query.KnockThrough, field, PropSet.Flimsy, buffer, b => field.Along(PropSet.Flimsy, a, z, 0f, b));
                Note(most, Query.Raycast, field, PropSet.ShellStoppers, buffer, b => field.Along(PropSet.ShellStoppers, a, z, 0f, b));
            }
            for (int k = 0; k < 8; k++)   // sight lines all round, as far as the tank sees
            {
                Vector2 end = at + Angles.Forward(k * Angles.TwoPi / 8) * tank.Spec.ViewRange;
                Note(most, Query.Raycast, field, PropSet.ShellStoppers, buffer, b => field.Along(PropSet.ShellStoppers, at, end, 0f, b));
            }
        }
        throw new InvalidOperationException($"stuck at {sim.Tanks[0].Position} on the way to {path[next]}");
    }

    /// <summary>
    /// Raises <paramref name="most"/>[<paramref name="query"/>] to the entries <paramref name="ask"/> gathers, if more: the
    /// smallest buffer it answers in without falling back to <see cref="Battlefield.All"/> (searched only above the record).
    /// </summary>
    private static void Note(int[] most, Query query, Battlefield field, PropSet set, int[] buffer, Ask ask)
    {
        int record = most[(int)query];
        if (Fits(record)) return;
        int low = record + 1, high = buffer.Length;   // it fits in high (the whole set) and not in record
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (Fits(middle)) high = middle;
            else low = middle + 1;
        }
        most[(int)query] = low;

        bool Fits(int size) => ask(buffer.AsSpan(0, size)) != field.All(set);
    }
}
