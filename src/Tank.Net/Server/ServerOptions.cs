using Tank.Core.World;
using Tank.Net.Protocol;

namespace Tank.Net.Server;

public sealed record ServerOptions
{
    public string RoomName { get; init; } = "Tank 3D";
    public int Port { get; init; } = ProtocolInfo.DefaultPort;
    public int MaxPlayers { get; init; } = GameState.MaxPlayers;
    public GameMode Mode { get; init; } = GameMode.Coop;
    public string? MapName { get; init; }
    public int Stage { get; init; } = 1;
    public int EnemyTotal { get; init; } = 20;
    public double ReturnToLobbySeconds { get; init; } = 8;
    public double ReconnectGraceSeconds { get; init; } = 30;
    /// <summary>
    /// Session token of the player running a listen server. Whoever says hello with it is host, regardless of
    /// who connected first (bots or fast LAN clients can beat the local player to the server).
    /// </summary>
    public uint? OwnerToken { get; init; }

    /// <summary>Fixed seed for reproducible matches (tests); null picks a random one per match.</summary>
    public uint? Seed { get; init; }
}
