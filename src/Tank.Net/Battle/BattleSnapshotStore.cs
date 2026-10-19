namespace Tank.Net.Battle;

/// <summary>Ring of recently received snapshots, used as delta baselines and for interpolation.</summary>
public sealed class BattleSnapshotStore
{
    public const int Size = BattleSeat.HistorySize;

    private readonly BattleSnapshot[] _ring = new BattleSnapshot[Size];

    public BattleSnapshotStore()
    {
        for (int i = 0; i < Size; i++) _ring[i] = new BattleSnapshot();
    }

    public int NewestTick { get; private set; } = -1;

    public BattleSnapshot? Newest => Find(NewestTick);

    public void Clear()
    {
        foreach (BattleSnapshot s in _ring) s.Tick = -1;
        NewestTick = -1;
    }

    public BattleSnapshot? Find(int tick) => tick >= 0 && _ring[tick % Size].Tick == tick ? _ring[tick % Size] : null;

    /// <summary>Stores a copy. Returns false for packets too old to be useful.</summary>
    public bool Add(BattleSnapshot decoded)
    {
        if (NewestTick >= 0 && decoded.Tick <= NewestTick - Size) return false;
        _ring[decoded.Tick % Size].CopyFrom(decoded);
        if (decoded.Tick > NewestTick) NewestTick = decoded.Tick;
        return true;
    }

    /// <summary>Newest stored snapshot with tick &lt;= <paramref name="tick"/>.</summary>
    public BattleSnapshot? FindAtOrBefore(int tick)
    {
        for (int t = System.Math.Min(tick, NewestTick); t > NewestTick - Size && t >= 0; t--)
        {
            BattleSnapshot? s = Find(t);
            if (s is not null) return s;
        }
        return null;
    }

    /// <summary>Oldest stored snapshot with tick &gt; <paramref name="tick"/>.</summary>
    public BattleSnapshot? FindAfter(int tick)
    {
        for (int t = System.Math.Max(tick + 1, NewestTick - Size + 1); t <= NewestTick; t++)
        {
            BattleSnapshot? s = Find(t);
            if (s is not null) return s;
        }
        return null;
    }
}
