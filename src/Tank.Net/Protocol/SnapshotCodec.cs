using Tank.Core.World;

namespace Tank.Net.Protocol;

/// <summary>
/// Encodes snapshots as deltas against a snapshot the receiver has acknowledged (Quake 3 model).
/// Lost packets never need resending: the next snapshot is simply encoded against an older acked baseline.
/// Instances hold scratch buffers and are not thread-safe.
/// </summary>
public sealed class SnapshotCodec
{
    private const byte MaskMeta = 1, MaskX = 2, MaskZ = 4, MaskDir = 8, MaskHp = 16, MaskFlags = 32, MaskAux = 64;
    private const byte MaskAll = 127;

    private readonly NetEntity[] _changed = new NetEntity[Snapshot.MaxEntities];
    private readonly int[] _removed = new int[Snapshot.MaxEntities];

    public void Write(BitWriter w, Snapshot current, Snapshot? baseline, in ClientHeader header)
    {
        w.WriteByte((byte)MessageType.Snapshot);
        w.WriteVarUInt((uint)current.Tick);
        w.WriteVarUInt(baseline is null ? 0u : (uint)baseline.Tick);
        WriteHeader(w, header);
        WriteMatch(w, current);

        var cur = current.EntitySpan;
        var bas = baseline is null ? ReadOnlySpan<NetEntity>.Empty : baseline.EntitySpan;

        int prevId = 0, bi = 0;
        foreach (ref readonly var e in cur)
        {
            while (bi < bas.Length && bas[bi].Id < e.Id) bi++;
            bool hasBase = bi < bas.Length && bas[bi].Id == e.Id;
            NetEntity b = hasBase ? bas[bi] : default;
            byte mask = hasBase ? DiffMask(e, b) : MaskAll;
            if (mask == 0) continue;
            w.WriteBool(true);
            w.WriteVarUInt((uint)(e.Id - prevId));
            prevId = e.Id;
            w.WriteBits(mask, 7);
            WriteFields(w, e, b, mask);
        }
        w.WriteBool(false);

        // Removed: in baseline but not in current.
        prevId = 0;
        int ci = 0;
        foreach (ref readonly var b in bas)
        {
            while (ci < cur.Length && cur[ci].Id < b.Id) ci++;
            if (ci < cur.Length && cur[ci].Id == b.Id) continue;
            w.WriteBool(true);
            w.WriteVarUInt((uint)(b.Id - prevId));
            prevId = b.Id;
        }
        w.WriteBool(false);
    }

    /// <summary>
    /// Decodes into <paramref name="output"/>. The message type byte must already be consumed.
    /// Throws <see cref="ProtocolException"/> if the referenced baseline is unavailable.
    /// </summary>
    public void Read(ref BitReader r, Func<int, Snapshot?> findBaseline, Snapshot output, out ClientHeader header, out int baseTick)
    {
        int tick = (int)r.ReadVarUInt();
        baseTick = (int)r.ReadVarUInt();
        Snapshot? baseline = null;
        if (baseTick != 0)
        {
            baseline = findBaseline(baseTick) ?? throw new ProtocolException($"Missing baseline {baseTick}.");
        }
        header = ReadHeader(ref r);
        output.Tick = tick;
        ReadMatch(ref r, output);

        var bas = baseline is null ? ReadOnlySpan<NetEntity>.Empty : baseline.EntitySpan;
        int changedCount = 0, removedCount = 0, prevId = 0, bi = 0;
        while (r.ReadBool())
        {
            if (changedCount >= _changed.Length) throw new ProtocolException("Too many entities.");
            int id = NextId(prevId, r.ReadVarUInt());
            prevId = id;
            byte mask = (byte)r.ReadBits(7);
            while (bi < bas.Length && bas[bi].Id < id) bi++;
            bool hasBase = bi < bas.Length && bas[bi].Id == id;
            if (!hasBase && mask != MaskAll) throw new ProtocolException($"Delta for unknown entity {id}.");
            NetEntity e = hasBase ? bas[bi] : default;
            e.Id = id;
            ReadFields(ref r, ref e, mask, hasBase);
            _changed[changedCount++] = e;
        }
        prevId = 0;
        while (r.ReadBool())
        {
            if (removedCount >= _removed.Length) throw new ProtocolException("Too many removals.");
            prevId = NextId(prevId, r.ReadVarUInt());
            _removed[removedCount++] = prevId;
        }

        Merge(bas, _changed.AsSpan(0, changedCount), _removed.AsSpan(0, removedCount), output);
    }

    /// <summary>Ids are written as strictly increasing gaps; anything else would break sorted lookups.</summary>
    private static int NextId(int prevId, uint gap)
    {
        long id = (long)prevId + gap;
        if (gap == 0 || id > int.MaxValue) throw new ProtocolException("Invalid entity id sequence.");
        return (int)id;
    }

    private static void Merge(ReadOnlySpan<NetEntity> bas, ReadOnlySpan<NetEntity> changed, ReadOnlySpan<int> removed, Snapshot output)
    {
        int n = 0, bi = 0, ci = 0, ri = 0;
        while (bi < bas.Length || ci < changed.Length)
        {
            if (n >= Snapshot.MaxEntities) throw new ProtocolException("Snapshot overflow.");
            bool takeChanged = ci < changed.Length && (bi >= bas.Length || changed[ci].Id <= bas[bi].Id);
            if (takeChanged)
            {
                if (bi < bas.Length && bas[bi].Id == changed[ci].Id) bi++;
                output.Entities[n++] = changed[ci++];
                continue;
            }
            int id = bas[bi].Id;
            while (ri < removed.Length && removed[ri] < id) ri++;
            if (ri < removed.Length && removed[ri] == id)
            {
                bi++;
                continue;
            }
            output.Entities[n++] = bas[bi++];
        }
        output.EntityCount = n;
    }

    private static byte DiffMask(in NetEntity e, in NetEntity b)
    {
        byte m = 0;
        if (e.Kind != b.Kind || e.Type != b.Type || e.Team != b.Team || e.Slot != b.Slot) m |= MaskMeta;
        if (e.X != b.X) m |= MaskX;
        if (e.Z != b.Z) m |= MaskZ;
        if (e.Dir != b.Dir) m |= MaskDir;
        if (e.Hp != b.Hp) m |= MaskHp;
        if (e.Flags != b.Flags) m |= MaskFlags;
        if (e.Aux != b.Aux) m |= MaskAux;
        return m;
    }

    private static void WriteFields(BitWriter w, in NetEntity e, in NetEntity b, byte mask)
    {
        if ((mask & MaskMeta) != 0)
        {
            w.WriteBits((uint)e.Kind, 2);
            w.WriteByte(e.Type);
            w.WriteBits(e.Team, 8);
            w.WriteByte(e.Slot);
        }
        if ((mask & MaskX) != 0) w.WriteVarInt(e.X - b.X);
        if ((mask & MaskZ) != 0) w.WriteVarInt(e.Z - b.Z);
        if ((mask & MaskDir) != 0) w.WriteBits(e.Dir, 2);
        if ((mask & MaskHp) != 0) w.WriteByte(e.Hp);
        if ((mask & MaskFlags) != 0) w.WriteByte((byte)e.Flags);
        if ((mask & MaskAux) != 0) w.WriteVarInt(e.Aux - b.Aux);
    }

    private static void ReadFields(ref BitReader r, ref NetEntity e, byte mask, bool hasBase)
    {
        if (!hasBase)
        {
            e.X = e.Z = e.Aux = 0;
        }
        if ((mask & MaskMeta) != 0)
        {
            e.Kind = (EntityKind)r.ReadBits(2);
            e.Type = r.ReadByte();
            e.Team = (byte)r.ReadBits(8);
            e.Slot = r.ReadByte();
        }
        if ((mask & MaskX) != 0) e.X += r.ReadVarInt();
        if ((mask & MaskZ) != 0) e.Z += r.ReadVarInt();
        if ((mask & MaskDir) != 0) e.Dir = (byte)r.ReadBits(2);
        if ((mask & MaskHp) != 0) e.Hp = r.ReadByte();
        if ((mask & MaskFlags) != 0) e.Flags = (EntityFlags)r.ReadByte();
        if ((mask & MaskAux) != 0) e.Aux += r.ReadVarInt();
    }

    private static void WriteHeader(BitWriter w, in ClientHeader h)
    {
        w.WriteVarInt(h.LastProcessedInputTick);
        w.WriteVarInt(h.InputSlack);
        w.WriteByte(h.OwnSlideTicks);
        w.WriteByte(h.OwnSpawnTicks);
        w.WriteByte(h.OwnFireCooldown);
    }

    private static ClientHeader ReadHeader(ref BitReader r) => new()
    {
        LastProcessedInputTick = r.ReadVarInt(),
        InputSlack = r.ReadVarInt(),
        OwnSlideTicks = r.ReadByte(),
        OwnSpawnTicks = r.ReadByte(),
        OwnFireCooldown = r.ReadByte(),
    };

    private static void WriteMatch(BitWriter w, Snapshot s)
    {
        w.WriteBits((uint)s.Phase, 1);
        w.WriteByte(s.WinnerTeam);
        w.WriteVarUInt((uint)s.EnemiesRemaining);
        w.WriteVarUInt((uint)s.TicksRemaining);
        w.WriteBits(s.MapChecksum, 32);
        foreach (ref readonly var p in s.Players.AsSpan())
        {
            w.WriteBool(p.Active);
            if (!p.Active) continue;
            w.WriteBits(p.Team, 4);
            w.WriteBool(p.Eliminated);
            w.WriteVarUInt(p.Lives);
            w.WriteVarUInt((uint)p.Score);
            w.WriteVarUInt((uint)p.Kills);
            w.WriteVarUInt((uint)p.Deaths);
            w.WriteVarUInt((uint)p.TankId);
        }
    }

    private static void ReadMatch(ref BitReader r, Snapshot s)
    {
        s.Phase = (MatchPhase)r.ReadBits(1);
        s.WinnerTeam = r.ReadByte();
        s.EnemiesRemaining = (int)r.ReadVarUInt();
        s.TicksRemaining = (int)r.ReadVarUInt();
        s.MapChecksum = r.ReadBits(32);
        for (int i = 0; i < s.Players.Length; i++)
        {
            ref var p = ref s.Players[i];
            p = default;
            p.Active = r.ReadBool();
            if (!p.Active) continue;
            p.Team = (byte)r.ReadBits(4);
            p.Eliminated = r.ReadBool();
            p.Lives = (byte)r.ReadVarUInt();
            p.Score = (int)r.ReadVarUInt();
            p.Kills = (int)r.ReadVarUInt();
            p.Deaths = (int)r.ReadVarUInt();
            p.TankId = (int)r.ReadVarUInt();
        }
    }
}
