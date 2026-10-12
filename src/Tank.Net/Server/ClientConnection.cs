namespace Tank.Net.Server;

/// <summary>Server-side view of one player. Survives a disconnect for the reconnect grace period.</summary>
internal sealed class ClientConnection
{
    public ClientConnection(byte slot, uint token)
    {
        Slot = slot;
        Token = token;
    }

    public byte Slot { get; }
    public uint Token { get; }
    public int PeerId { get; set; } = -1;
    public string Name { get; set; } = "Player";
    public byte Team { get; set; }
    public bool Ready { get; set; }
    public int AckedSnapshotTick { get; set; }
    public int DisconnectedAtTick { get; set; } = -1;
    public long LastResyncFrame { get; set; } = long.MinValue / 2;
    public ServerInputBuffer Inputs { get; } = new();

    public bool Connected => PeerId >= 0;
}
