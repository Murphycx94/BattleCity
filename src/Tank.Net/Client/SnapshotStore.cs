using Tank.Net.Protocol;

namespace Tank.Net.Client;

/// <summary>Ring of recently received snapshots, used as delta baselines and for interpolation.</summary>
public sealed class SnapshotStore
{
    public const int Size = 64;

    private readonly Snapshot[] _ring = new Snapshot[Size];

    public SnapshotStore()
    {
        for (int i = 0; i < Size; i++) _ring[i] = new Snapshot { Tick = -1 };
        NewestTick = -1;
    }

    public int NewestTick { get; private set; }

    public Snapshot? Newest => Find(NewestTick);

    public void Clear()
    {
        foreach (var s in _ring) s.Tick = -1;
        NewestTick = -1;
    }

    public Snapshot? Find(int tick) => tick >= 0 && _ring[tick % Size].Tick == tick ? _ring[tick % Size] : null;

    /// <summary>Stores a copy. Returns false for packets too old to be useful.</summary>
    public bool Add(Snapshot decoded)
    {
        if (NewestTick >= 0 && decoded.Tick <= NewestTick - Size) return false;
        _ring[decoded.Tick % Size].CopyFrom(decoded);
        if (decoded.Tick > NewestTick) NewestTick = decoded.Tick;
        return true;
    }

    /// <summary>Newest stored snapshot with tick &lt;= <paramref name="tick"/>.</summary>
    public Snapshot? FindAtOrBefore(int tick)
    {
        for (int t = System.Math.Min(tick, NewestTick); t > NewestTick - Size && t >= 0; t--)
        {
            var s = Find(t);
            if (s is not null) return s;
        }
        return null;
    }

    /// <summary>Oldest stored snapshot with tick &gt; <paramref name="tick"/>.</summary>
    public Snapshot? FindAfter(int tick)
    {
        for (int t = System.Math.Max(tick + 1, NewestTick - Size + 1); t <= NewestTick; t++)
        {
            var s = Find(t);
            if (s is not null) return s;
        }
        return null;
    }
}
