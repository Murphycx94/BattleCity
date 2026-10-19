using Tank.Net.Battle;
using Tank.Net.Protocol;

namespace Tank.Net.Tests.Battle;

/// <summary>Builds the packets a server would send, and hostile variations of them, for the client tests.</summary>
internal static class BattleTestPackets
{
    /// <summary>A full snapshot of <paramref name="tick"/>: the template with <paramref name="mutate"/> applied, acking <paramref name="lastProcessed"/>.</summary>
    public static byte[] Snapshot(BattleSnapshot template, int tick, int lastProcessed, Action<BattleSnapshot>? mutate = null, int slack = 2)
    {
        var s = new BattleSnapshot();
        s.CopyFrom(template);
        s.Tick = tick;
        mutate?.Invoke(s);
        var w = new BitWriter(4096);
        new BattleSnapshotCodec().Write(w, s, null, new ClientFeedback { LastProcessedInputTick = lastProcessed, InputSlack = slack });
        return w.Data.ToArray();
    }

    public static byte[] Events(Tank.Net.Battle.EventBatch batch)
    {
        var w = new BitWriter(2048);
        BattleMessages.Write(w, batch);
        return w.Data.ToArray();
    }

    public static byte[] MatchStart(Tank.Net.Battle.MatchStartMessage message)
    {
        var w = new BitWriter(1 << 17);
        BattleMessages.Write(w, message);
        return w.Data.ToArray();
    }

    /// <summary>Adds (or replaces) an entity, keeping the list sorted by id as the codec needs it.</summary>
    public static void SetEntity(BattleSnapshot s, BattleEntity entity)
    {
        var list = s.EntitySpan.ToArray().Where(e => e.Id != entity.Id).Append(entity).OrderBy(e => e.Id).ToList();
        for (int i = 0; i < list.Count; i++) s.Entities[i] = list[i];
        s.EntityCount = list.Count;
    }

    /// <summary>A snapshot holding just these entities, for the interpolator.</summary>
    public static BattleSnapshot Of(int tick, params BattleEntity[] entities)
    {
        var s = new BattleSnapshot { Tick = tick, EntityCount = entities.Length };
        Array.Copy(entities.OrderBy(e => e.Id).ToArray(), s.Entities, entities.Length);
        return s;
    }

    public static BattleEntity Entity(int id, int xCm, int zCm) => new() { Id = id, Slot = (byte)id, Spec = 1, Team = 1, X = xCm, Z = zCm, Health = 100 };
}
