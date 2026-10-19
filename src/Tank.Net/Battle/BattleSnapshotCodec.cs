using Tank.Net.Protocol;
using Tank.Sim.Combat;
using Tank.Sim.Modes;

namespace Tank.Net.Battle;

/// <summary>
/// Encodes a client's snapshot as a delta against a snapshot it has acknowledged (Quake 3 model): entities that
/// changed carry a mask of changed fields, new ones every field, those gone a removal; the own tank and the
/// scoreboard go in full when they changed. A lost packet never needs resending: the next snapshot is encoded against
/// an older acknowledged baseline. Instances hold scratch buffers and are not thread-safe.
/// </summary>
public sealed class BattleSnapshotCodec
{
    private const ushort MaskMeta = 1, MaskX = 2, MaskZ = 4, MaskYaw = 8, MaskTurret = 16, MaskPitch = 32, MaskSpeed = 64, MaskHealth = 128, MaskFlags = 256;
    private const ushort MaskAll = 511;
    private const int MaskBits = 9;

    private readonly BattleEntity[] _changed = new BattleEntity[BattleSnapshot.MaxEntities];
    private readonly int[] _removed = new int[BattleSnapshot.MaxEntities];

    public void Write(BitWriter w, BattleSnapshot current, BattleSnapshot? baseline, in ClientFeedback feedback)
    {
        w.WriteByte((byte)BattleMessage.Snapshot);
        w.WriteVarUInt((uint)current.Tick);
        w.WriteVarUInt(baseline is null ? 0u : (uint)baseline.Tick);
        w.WriteVarInt(feedback.LastProcessedInputTick);
        w.WriteVarInt(feedback.InputSlack);
        WriteMatch(w, current.Match);

        w.WriteBool(current.HasOwn);
        if (current.HasOwn)
        {
            bool same = baseline is { HasOwn: true } && baseline.Own.Equals(current.Own);
            w.WriteBool(same);
            if (!same) WriteOwn(w, current.Own);
        }

        bool sameStats = baseline is not null && baseline.Players.AsSpan().SequenceEqual(current.Players);
        w.WriteBool(sameStats);
        if (!sameStats) WriteStats(w, current.Players);

        ReadOnlySpan<BattleEntity> cur = current.EntitySpan;
        ReadOnlySpan<BattleEntity> bas = baseline is null ? ReadOnlySpan<BattleEntity>.Empty : baseline.EntitySpan;
        int prevId = 0, bi = 0;
        foreach (ref readonly BattleEntity e in cur)
        {
            while (bi < bas.Length && bas[bi].Id < e.Id) bi++;
            bool hasBase = bi < bas.Length && bas[bi].Id == e.Id;
            BattleEntity b = hasBase ? bas[bi] : default;
            ushort mask = hasBase ? DiffMask(e, b) : MaskAll;
            if (mask == 0) continue;
            w.WriteBool(true);
            w.WriteVarUInt((uint)(e.Id - prevId));
            prevId = e.Id;
            w.WriteBits(mask, MaskBits);
            WriteFields(w, e, b, mask);
        }
        w.WriteBool(false);

        prevId = 0;
        int ci = 0;
        foreach (ref readonly BattleEntity b in bas)
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
    /// Decodes into <paramref name="output"/> (which must not be the baseline itself); the type byte must already be
    /// consumed. Throws if the baseline referenced is unavailable.
    /// </summary>
    public void Read(ref BitReader r, Func<int, BattleSnapshot?> findBaseline, BattleSnapshot output, out ClientFeedback feedback, out int baseTick)
    {
        int tick = BattleMessages.ReadTick(ref r);
        baseTick = BattleMessages.ReadTick(ref r);
        BattleSnapshot? baseline = null;
        if (baseTick != 0) baseline = findBaseline(baseTick) ?? throw new ProtocolException($"Missing baseline {baseTick}.");
        feedback = new ClientFeedback { LastProcessedInputTick = r.ReadVarInt(), InputSlack = r.ReadVarInt() };
        output.Tick = tick;
        output.Match = ReadMatch(ref r);

        output.HasOwn = r.ReadBool();
        if (output.HasOwn)
        {
            bool same = r.ReadBool();
            if (same)
            {
                if (baseline is not { HasOwn: true }) throw new ProtocolException("Own tank refers to a baseline without one.");
                output.Own = baseline.Own;
            }
            else output.Own = ReadOwn(ref r);
        }
        else output.Own = default;

        if (r.ReadBool())
        {
            if (baseline is null) throw new ProtocolException("Scoreboard refers to a missing baseline.");
            Array.Copy(baseline.Players, output.Players, output.Players.Length);
        }
        else ReadStats(ref r, output.Players);

        ReadOnlySpan<BattleEntity> bas = baseline is null ? ReadOnlySpan<BattleEntity>.Empty : baseline.EntitySpan;
        int changedCount = 0, removedCount = 0, prevId = 0, bi = 0;
        while (r.ReadBool())
        {
            if (changedCount >= _changed.Length) throw new ProtocolException("Too many entities.");
            int id = NextId(prevId, r.ReadVarUInt());
            prevId = id;
            ushort mask = (ushort)r.ReadBits(MaskBits);
            while (bi < bas.Length && bas[bi].Id < id) bi++;
            bool hasBase = bi < bas.Length && bas[bi].Id == id;
            if (!hasBase && mask != MaskAll) throw new ProtocolException($"Delta for unknown entity {id}.");
            BattleEntity e = hasBase ? bas[bi] : default;
            e.Id = id;
            ReadFields(ref r, ref e, mask);
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

    private static int NextId(int prevId, uint gap)
    {
        long id = (long)prevId + gap;
        if (gap == 0 || id > int.MaxValue) throw new ProtocolException("Invalid entity id sequence.");
        return (int)id;
    }

    private static void Merge(ReadOnlySpan<BattleEntity> bas, ReadOnlySpan<BattleEntity> changed, ReadOnlySpan<int> removed, BattleSnapshot output)
    {
        int n = 0, bi = 0, ci = 0, ri = 0;
        while (bi < bas.Length || ci < changed.Length)
        {
            if (n >= BattleSnapshot.MaxEntities) throw new ProtocolException("Snapshot overflow.");
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

    private static ushort DiffMask(in BattleEntity e, in BattleEntity b)
    {
        ushort m = 0;
        if (e.Slot != b.Slot || e.Spec != b.Spec || e.Team != b.Team) m |= MaskMeta;
        if (e.X != b.X) m |= MaskX;
        if (e.Z != b.Z) m |= MaskZ;
        if (e.Yaw != b.Yaw) m |= MaskYaw;
        if (e.TurretYaw != b.TurretYaw) m |= MaskTurret;
        if (e.Pitch != b.Pitch) m |= MaskPitch;
        if (e.Speed != b.Speed) m |= MaskSpeed;
        if (e.Health != b.Health) m |= MaskHealth;
        if (e.Flags != b.Flags) m |= MaskFlags;
        return m;
    }

    private static void WriteFields(BitWriter w, in BattleEntity e, in BattleEntity b, ushort mask)
    {
        if ((mask & MaskMeta) != 0)
        {
            w.WriteByte(e.Slot);
            w.WriteBits(e.Spec, 2);
            w.WriteBits(e.Team, 4);
        }
        if ((mask & MaskX) != 0) w.WriteVarInt(e.X - b.X);
        if ((mask & MaskZ) != 0) w.WriteVarInt(e.Z - b.Z);
        if ((mask & MaskYaw) != 0) w.WriteBits(e.Yaw, BattleProtocol.YawBits);
        if ((mask & MaskTurret) != 0) w.WriteBits(e.TurretYaw, BattleProtocol.YawBits);
        if ((mask & MaskPitch) != 0) w.WriteBits(e.Pitch, BattleProtocol.PitchBits);
        if ((mask & MaskSpeed) != 0) w.WriteVarInt(e.Speed);
        if ((mask & MaskHealth) != 0) w.WriteVarUInt(e.Health);
        if ((mask & MaskFlags) != 0) w.WriteBits((uint)e.Flags, 2);
    }

    private static void ReadFields(ref BitReader r, ref BattleEntity e, ushort mask)
    {
        if ((mask & MaskMeta) != 0)
        {
            e.Slot = r.ReadByte();
            e.Spec = (byte)r.ReadBits(2);
            e.Team = (byte)r.ReadBits(4);
        }
        if ((mask & MaskX) != 0) e.X = AddCoordinate(e.X, r.ReadVarInt());
        if ((mask & MaskZ) != 0) e.Z = AddCoordinate(e.Z, r.ReadVarInt());
        if ((mask & MaskYaw) != 0) e.Yaw = (ushort)r.ReadBits(BattleProtocol.YawBits);
        if ((mask & MaskTurret) != 0) e.TurretYaw = (ushort)r.ReadBits(BattleProtocol.YawBits);
        if ((mask & MaskPitch) != 0) e.Pitch = (ushort)r.ReadBits(BattleProtocol.PitchBits);
        if ((mask & MaskSpeed) != 0) e.Speed = (short)InRange(r.ReadVarInt(), short.MinValue, short.MaxValue);
        if ((mask & MaskHealth) != 0) e.Health = (ushort)InRange(BattleMessages.ReadNonNegative(ref r), 0, ushort.MaxValue);
        if ((mask & MaskFlags) != 0) e.Flags = (TankFlags)r.ReadBits(2);
    }

    private static void WriteMatch(BitWriter w, in MatchBlock m)
    {
        w.WriteBits((uint)m.Phase, 1);
        w.WriteVarUInt((uint)m.TicksLeft);
        w.WriteBool(m.Spotted);
        w.WriteByte(m.WinnerTeam);
        w.WriteByte(m.WinnerSlot);
        w.WriteVarUInt((uint)m.RespawnIn);
        w.WriteVarUInt((uint)m.TeamScore0);
        w.WriteVarUInt((uint)m.TeamScore1);
    }

    private static MatchBlock ReadMatch(ref BitReader r) => new()
    {
        Phase = (MatchPhase)r.ReadBits(1),
        TicksLeft = BattleMessages.ReadNonNegative(ref r),
        Spotted = r.ReadBool(),
        WinnerTeam = r.ReadByte(),
        WinnerSlot = r.ReadByte(),
        RespawnIn = BattleMessages.ReadNonNegative(ref r),
        TeamScore0 = BattleMessages.ReadNonNegative(ref r),
        TeamScore1 = BattleMessages.ReadNonNegative(ref r),
    };

    /// <summary>A coordinate plus its delta, which must stay within the map's world (no overflow, no 100 km tanks).</summary>
    private static int AddCoordinate(int coordinate, int delta)
    {
        long sum = (long)coordinate + delta;
        return System.Math.Abs(sum) <= BattleProtocol.MaxCoordinateCm ? (int)sum : throw new ProtocolException("Coordinate out of range.");
    }

    private static float CheckedCoordinate(ref BitReader r)
    {
        float value = BattleMessages.ReadFloat(ref r);
        return MathF.Abs(value) <= BattleProtocol.MaxCoordinateCm * BattleProtocol.PositionUnit ? value : throw new ProtocolException("Coordinate out of range.");
    }

    private static int InRange(long value, int min, int max) =>
        value >= min && value <= max ? (int)value : throw new ProtocolException("Value out of range.");

    private static void WriteOwn(BitWriter w, in OwnTank o)
    {
        w.WriteVarUInt((uint)o.Id);
        BattleMessages.WriteFloat(w, o.X);
        BattleMessages.WriteFloat(w, o.Z);
        BattleMessages.WriteFloat(w, o.Yaw);
        BattleMessages.WriteFloat(w, o.TurretYaw);
        BattleMessages.WriteFloat(w, o.GunPitch);
        BattleMessages.WriteFloat(w, o.Speed);
        BattleMessages.WriteFloat(w, o.Reload);
        BattleMessages.WriteFloat(w, o.Bloom);
        w.WriteBits(o.Ammo == AmmoType.HE ? 1u : 0u, 1);
        w.WriteVarUInt((uint)o.ApLeft);
        w.WriteVarUInt((uint)o.HeLeft);
        w.WriteVarUInt((uint)o.ShotCount);
        w.WriteVarUInt((uint)o.Health);
        w.WriteVarInt(o.ProtectedUntil);
        w.WriteVarInt(o.LastFired);
        w.WriteBool(o.Destroyed);
    }

    private static OwnTank ReadOwn(ref BitReader r) => new()
    {
        Id = BattleMessages.ReadNonNegative(ref r),
        X = CheckedCoordinate(ref r),
        Z = CheckedCoordinate(ref r),
        Yaw = BattleMessages.ReadFloat(ref r),
        TurretYaw = BattleMessages.ReadFloat(ref r),
        GunPitch = BattleMessages.ReadFloat(ref r),
        Speed = BattleMessages.ReadFloat(ref r),
        Reload = BattleMessages.ReadFloat(ref r),
        Bloom = BattleMessages.ReadFloat(ref r),
        Ammo = r.ReadBits(1) == 1 ? AmmoType.HE : AmmoType.AP,
        ApLeft = BattleMessages.ReadNonNegative(ref r),
        HeLeft = BattleMessages.ReadNonNegative(ref r),
        ShotCount = BattleMessages.ReadNonNegative(ref r),
        Health = BattleMessages.ReadNonNegative(ref r),
        ProtectedUntil = r.ReadVarInt(),
        LastFired = r.ReadVarInt(),
        Destroyed = r.ReadBool(),
    };

    private static void WriteStats(BitWriter w, PlayerStats[] players)
    {
        foreach (ref readonly PlayerStats p in players.AsSpan())
        {
            w.WriteBool(p.Active);
            if (!p.Active) continue;
            w.WriteBits(p.Team, 4);
            w.WriteBits(p.Spec, 2);
            w.WriteVarUInt((uint)p.TankId);
            w.WriteVarUInt((uint)p.Kills);
            w.WriteVarUInt((uint)p.Deaths);
            w.WriteBool(p.Alive);
            w.WriteBits(p.Rank, 4);
        }
    }

    private static void ReadStats(ref BitReader r, PlayerStats[] players)
    {
        for (int i = 0; i < players.Length; i++)
        {
            ref PlayerStats p = ref players[i];
            p = default;
            p.Active = r.ReadBool();
            if (!p.Active) continue;
            p.Team = (byte)r.ReadBits(4);
            p.Spec = (byte)r.ReadBits(2);
            p.TankId = BattleMessages.ReadNonNegative(ref r);
            p.Kills = BattleMessages.ReadNonNegative(ref r);
            p.Deaths = BattleMessages.ReadNonNegative(ref r);
            p.Alive = r.ReadBool();
            p.Rank = (byte)r.ReadBits(4);
        }
    }
}
