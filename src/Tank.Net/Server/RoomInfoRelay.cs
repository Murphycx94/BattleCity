using Tank.Net.Protocol;
using Tank.Net.Transport;

namespace Tank.Net.Server;

/// <summary>Breaks the construction cycle transport -> room info -> server -> transport.</summary>
public sealed class RoomInfoRelay : IRoomInfoProvider
{
    private readonly Func<GameServer> _server;

    public RoomInfoRelay(Func<GameServer> server) => _server = server;

    public RoomInfoMessage GetRoomInfo() => _server().GetRoomInfo();
}
