using Tank.Core.World;
using Tank.Net.Protocol;

namespace Tank.Net.Tests;

public sealed class SnapshotCodecTests
{
    private static Snapshot Make(int tick, params NetEntity[] entities)
    {
        var s = new Snapshot { Tick = tick, EnemiesRemaining = 7, MapChecksum = 1234 };
        entities.CopyTo(s.Entities, 0);
        s.EntityCount = entities.Length;
        s.Players[0] = new PlayerInfo { Active = true, Team = 0, Lives = 3, Score = 400, Kills = 2, TankId = 1 };
        return s;
    }

    private static NetEntity Tank(int id, int x, int z) => new()
    {
        Id = id, Kind = EntityKind.Tank, Type = 1, Team = 8, Slot = 255, X = x, Z = z, Dir = 2, Hp = 1,
        Flags = EntityFlags.Moving, Aux = 0,
    };

    private static byte[] Encode(Snapshot cur, Snapshot? baseline, ClientHeader header = default)
    {
        var w = new BitWriter();
        new SnapshotCodec().Write(w, cur, baseline, header);
        return w.Data.ToArray();
    }

    private static Snapshot Decode(byte[] data, Func<int, Snapshot?> find, out ClientHeader header)
    {
        var r = new BitReader(data);
        Assert.Equal(MessageType.Snapshot, (MessageType)r.ReadByte());
        var output = new Snapshot();
        new SnapshotCodec().Read(ref r, find, output, out header, out _);
        return output;
    }

    [Fact]
    public void FullSnapshot_RoundTrips()
    {
        var snap = Make(10, Tank(1, 100, 200), Tank(5, -50, 7000));
        var header = new ClientHeader { LastProcessedInputTick = 10, InputSlack = -2, OwnSpawnTicks = 4 };

        var decoded = Decode(Encode(snap, null, header), _ => null, out var h);

        Assert.Equal(snap.EntitySpan.ToArray(), decoded.EntitySpan.ToArray());
        Assert.Equal(snap.Players, decoded.Players);
        Assert.Equal(7, decoded.EnemiesRemaining);
        Assert.Equal(1234u, decoded.MapChecksum);
        Assert.Equal(header, h);
    }

    [Fact]
    public void Delta_HandlesChangedAddedRemovedAndUnchanged()
    {
        var baseline = Make(10, Tank(1, 100, 200), Tank(2, 0, 0), Tank(3, 9, 9));
        var current = Make(12, Tank(1, 112, 200), Tank(3, 9, 9), Tank(4, 50, 60));

        var decoded = Decode(Encode(current, baseline), t => t == 10 ? baseline : null, out _);

        Assert.Equal(current.EntitySpan.ToArray(), decoded.EntitySpan.ToArray());
        Assert.Equal(12, decoded.Tick);
    }

    [Fact]
    public void Delta_IsMuchSmallerThanFull_WhenLittleChanges()
    {
        var entities = Enumerable.Range(1, 40).Select(i => Tank(i, i * 300, i * 100)).ToArray();
        var baseline = Make(10, entities);
        var moved = entities.ToArray();
        moved[3].X += 12;
        var current = Make(11, moved);

        int full = Encode(current, null).Length;
        int delta = Encode(current, baseline).Length;

        Assert.True(delta * 5 < full, $"delta {delta} bytes vs full {full} bytes");
    }

    [Fact]
    public void DuplicateIdsOnTheWire_AreRejected()
    {
        var data = Encode(Make(3, Tank(5, 0, 0), Tank(5, 1, 1)), null);
        Assert.Throws<ProtocolException>(() => Decode(data, _ => null, out _));
    }

    [Fact]
    public void Delta_WithMissingBaseline_Throws()
    {
        var baseline = Make(10, Tank(1, 0, 0));
        var data = Encode(Make(11, Tank(1, 5, 0)), baseline);
        Assert.Throws<ProtocolException>(() => Decode(data, _ => null, out _));
    }

    [Fact]
    public void DeltaChain_SurvivesDroppedSnapshots()
    {
        // Server always deltas against the newest *acknowledged* snapshot, so drops never corrupt state.
        var server = new Dictionary<int, Snapshot>();
        var client = new Dictionary<int, Snapshot>();
        int acked = 0;
        var rng = new Random(3);
        for (int tick = 1; tick <= 200; tick++)
        {
            var snap = Make(tick, Enumerable.Range(1, 10).Where(i => (i + tick / 20) % 4 != 0)
                .Select(i => Tank(i, i * 10 + tick, i)).ToArray());
            server[tick] = snap;
            var data = Encode(snap, acked > 0 ? server[acked] : null);
            if (rng.Next(100) < 30) continue; // 30% loss
            var decoded = Decode(data, t => client.GetValueOrDefault(t), out _);
            Assert.Equal(snap.EntitySpan.ToArray(), decoded.EntitySpan.ToArray());
            client[tick] = decoded;
            acked = tick;
        }
    }

    [Fact]
    public void Builder_ProjectsGameStateSortedById()
    {
        var sim = new Tank.Core.Simulation(new MatchConfig(GameMode.Coop, MapLibrary.Load("coop-01"), 1));
        sim.AddPlayer(0, 0);
        for (int i = 0; i < 200; i++) sim.Step(new InputCmd[8]);

        var snap = new Snapshot();
        SnapshotBuilder.Build(sim.State, snap);

        var ids = snap.EntitySpan.ToArray().Select(e => e.Id).ToArray();
        Assert.Equal(ids.OrderBy(i => i), ids);
        Assert.Contains(snap.EntitySpan.ToArray(), e => e.Kind == EntityKind.Eagle);
        Assert.Contains(snap.EntitySpan.ToArray(), e => e.Kind == EntityKind.Tank && e.Slot == 0);
        Assert.Equal(sim.State.Map.ComputeChecksum(), snap.MapChecksum);
        Assert.True(snap.TryFind(sim.State.Players[0].TankId, out _));
    }
}
