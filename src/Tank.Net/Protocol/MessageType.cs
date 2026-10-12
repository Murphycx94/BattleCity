namespace Tank.Net.Protocol;

public enum MessageType : byte
{
    // client -> server
    Hello = 1,
    Input = 2,
    LobbyCommand = 3,
    RequestResync = 4,

    // server -> client
    Welcome = 20,
    Reject = 21,
    LobbyState = 22,
    MatchStart = 23,
    Snapshot = 24,
    Events = 25,

    // unconnected (LAN discovery)
    Discover = 40,
    RoomInfo = 41,
}

public enum Channel : byte
{
    /// <summary>Inputs and snapshots: never retransmitted, newer data supersedes lost data.</summary>
    Unreliable = 0,
    /// <summary>Events, lobby and match control.</summary>
    Reliable = 1,
}

public static class ProtocolInfo
{
    public const int Version = 1;
    public const int DefaultPort = 27015;
    public const string ConnectKey = "tank3d-v1";
    public const int MaxPlayerNameBytes = 24;
    public const int MaxRedundantInputs = 4;
}
