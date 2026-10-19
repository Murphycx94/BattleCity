using System.Diagnostics;
using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using Tank.Net.Protocol;

namespace Tank.Net.Transport;

public sealed record DiscoveredRoom(IPEndPoint EndPoint, RoomInfoMessage Info, int PingMs, double LastSeenSeconds);

/// <summary>A battle room (stage 4) found on the local network.</summary>
public sealed record DiscoveredBattleRoom(IPEndPoint EndPoint, Battle.RoomInfoMessage Info, int PingMs, double LastSeenSeconds);

/// <summary>
/// Finds servers on the local network by UDP broadcast (docs/03-netcode.md §8.1): the old game's rooms in
/// <see cref="Rooms"/>, battle rooms (stage 4) in <see cref="BattleRooms"/>.
/// Call <see cref="Poll"/> regularly; rooms not heard from for a few seconds drop off the list.
/// </summary>
public sealed class LanDiscovery : IDisposable
{
    private const double BroadcastIntervalSeconds = 1.0;
    private const double RoomTimeoutSeconds = 3.5;

    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _manager;
    private readonly int _port;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, DiscoveredRoom> _rooms = new();
    private readonly Dictionary<string, DiscoveredBattleRoom> _battleRooms = new();
    private readonly NetDataWriter _packet = new();
    private double _lastBroadcast = double.NegativeInfinity;

    public LanDiscovery(int port = ProtocolInfo.DefaultPort)
    {
        _port = port;
        _manager = new NetManager(_listener) { UnconnectedMessagesEnabled = true, BroadcastReceiveEnabled = true };
        _listener.NetworkReceiveUnconnectedEvent += OnReply;
        if (!_manager.Start()) throw new InvalidOperationException("无法打开用于局域网搜索的 UDP 套接字。");

        var w = new BitWriter(16);
        Messages.WriteDiscover(w);
        _packet.Put(w.Data);
    }

    public IReadOnlyCollection<DiscoveredRoom> Rooms => _rooms.Values;

    public IReadOnlyCollection<DiscoveredBattleRoom> BattleRooms => _battleRooms.Values;

    public void Poll()
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (now - _lastBroadcast >= BroadcastIntervalSeconds)
        {
            _lastBroadcast = now;
            _manager.SendBroadcast(_packet, _port);
        }
        _manager.PollEvents();
        foreach (var key in _rooms.Where(kv => now - kv.Value.LastSeenSeconds > RoomTimeoutSeconds).Select(kv => kv.Key).ToList())
            _rooms.Remove(key);
        foreach (var key in _battleRooms.Where(kv => now - kv.Value.LastSeenSeconds > RoomTimeoutSeconds).Select(kv => kv.Key).ToList())
            _battleRooms.Remove(key);
    }

    public void Dispose() => _manager.Stop();

    private void OnReply(IPEndPoint remote, NetPacketReader reader, UnconnectedMessageType type)
    {
        try
        {
            var r = new BitReader(reader.GetRemainingBytesSpan());
            byte kind = r.ReadByte();
            double now = _clock.Elapsed.TotalSeconds;
            int ping = (int)System.Math.Max(0, (now - _lastBroadcast) * 1000);
            if (kind == (byte)MessageType.RoomInfo)
            {
                var info = Messages.ReadRoomInfo(ref r);
                var endpoint = new IPEndPoint(remote.Address, info.Port);
                _rooms[endpoint.ToString()] = new DiscoveredRoom(endpoint, info, ping, now);
            }
            else if (kind == (byte)Battle.BattleMessage.RoomInfo)
            {
                var info = Battle.BattleMessages.ReadRoomInfo(ref r);
                var endpoint = new IPEndPoint(remote.Address, info.Port);
                _battleRooms[endpoint.ToString()] = new DiscoveredBattleRoom(endpoint, info, ping, now);
            }
        }
        catch (ProtocolException)
        {
            // Unrelated traffic on the port: ignore.
        }
    }
}
