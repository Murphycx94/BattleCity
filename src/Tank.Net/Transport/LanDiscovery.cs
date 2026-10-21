using System.Diagnostics;
using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using Tank.Net.Battle;
using Tank.Net.Protocol;

namespace Tank.Net.Transport;

/// <summary>A room found on the local network.</summary>
public sealed record DiscoveredRoom(IPEndPoint EndPoint, RoomInfoMessage Info, int PingMs, double LastSeenSeconds);

/// <summary>
/// Finds rooms on the local network by UDP broadcast (<see cref="BattleMessage.Discover"/>, answered with
/// <see cref="BattleMessage.RoomInfo"/>). Call <see cref="Poll"/> regularly; rooms not heard from for a few seconds
/// drop off <see cref="Rooms"/>.
/// </summary>
public sealed class LanDiscovery : IDisposable
{
    private const double BroadcastIntervalSeconds = 1.0;
    private const double RoomTimeoutSeconds = 3.5;

    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _manager;
    private readonly int _port;
    private readonly IPEndPoint? _target;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, DiscoveredRoom> _rooms = new();
    private readonly NetDataWriter _packet = new();
    private double _lastBroadcast = double.NegativeInfinity;

    public LanDiscovery(int port = BattleProtocol.DefaultPort) : this(port, null)
    {
    }

    /// <param name="target">Where to send the request instead of broadcasting it (tests: a room on 127.0.0.1).</param>
    internal LanDiscovery(int port, IPEndPoint? target)
    {
        _port = port;
        _target = target;
        _manager = new NetManager(_listener) { UnconnectedMessagesEnabled = true, BroadcastReceiveEnabled = true };
        _listener.NetworkReceiveUnconnectedEvent += OnReply;
        if (!_manager.Start()) throw new InvalidOperationException("无法打开用于局域网搜索的 UDP 套接字。");

        var w = new BitWriter(16);
        BattleMessages.WriteDiscover(w);
        _packet.Put(w.Data);
    }

    public IReadOnlyCollection<DiscoveredRoom> Rooms => _rooms.Values;

    public void Poll()
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (now - _lastBroadcast >= BroadcastIntervalSeconds)
        {
            _lastBroadcast = now;
            if (_target is null) _manager.SendBroadcast(_packet, _port);
            else _manager.SendUnconnectedMessage(_packet, _target);
        }
        _manager.PollEvents();
        foreach (var key in _rooms.Where(kv => now - kv.Value.LastSeenSeconds > RoomTimeoutSeconds).Select(kv => kv.Key).ToList())
            _rooms.Remove(key);
    }

    public void Dispose() => _manager.Stop();

    private void OnReply(IPEndPoint remote, NetPacketReader reader, UnconnectedMessageType type)
    {
        try
        {
            var r = new BitReader(reader.GetRemainingBytesSpan());
            if (r.ReadByte() != (byte)BattleMessage.RoomInfo) return;
            var info = BattleMessages.ReadRoomInfo(ref r);
            double now = _clock.Elapsed.TotalSeconds;
            int ping = (int)System.Math.Max(0, (now - _lastBroadcast) * 1000);
            var endpoint = new IPEndPoint(remote.Address, info.Port);
            _rooms[endpoint.ToString()] = new DiscoveredRoom(endpoint, info, ping, now);
        }
        catch (ProtocolException)
        {
            // Unrelated traffic on the port: ignore.
        }
    }
}
