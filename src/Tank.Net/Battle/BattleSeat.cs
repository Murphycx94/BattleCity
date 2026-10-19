using Tank.Sim.Ai;

namespace Tank.Net.Battle;

/// <summary>A seat in the room: a player (surviving a disconnect for the reconnect grace period) or a bot.</summary>
internal sealed class BattleSeat
{
    public const int HistorySize = 64;

    public BattleSeat(byte slot, uint token, bool isBot)
    {
        Slot = slot;
        Token = token;
        IsBot = isBot;
        if (!isBot)
        {
            History = new BattleSnapshot[HistorySize];
            for (int i = 0; i < HistorySize; i++) History[i] = new BattleSnapshot();
        }
    }

    public byte Slot { get; }
    public uint Token { get; }
    public bool IsBot { get; }
    public int PeerId { get; set; } = -1;
    public string Name { get; set; } = "Player";
    public byte Team { get; set; }
    public byte TankIndex { get; set; } = 1;   // the medium tank
    public bool Ready { get; set; }
    public int AckedSnapshotTick { get; set; }
    public long DisconnectedAtFrame { get; set; } = -1;
    public long LastResyncFrame { get; set; } = long.MinValue / 2;
    public BattleInputBuffer Inputs { get; } = new();

    /// <summary>The snapshots sent to this player, by tick, for delta baselines (players only).</summary>
    public BattleSnapshot[]? History { get; }

    public SkirmishBot? Bot { get; set; }

    public bool Connected => PeerId >= 0;

    /// <summary>Starts the snapshot chain over: nothing sent so far may serve as a delta baseline.</summary>
    public void ForgetSnapshots()
    {
        AckedSnapshotTick = 0;
        if (History is null) return;
        foreach (BattleSnapshot s in History) s.Tick = -1;
    }

    /// <summary>Whether this seat is in the match (has a tank).</summary>
    public bool InMatch { get; set; }

    public void ResetForMatch()
    {
        Inputs.Reset();
        ForgetSnapshots();
    }
}
