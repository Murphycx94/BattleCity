using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using Tank.Net.Battle;
using Tank.Net.Protocol;

namespace Tank.Net.Transport;

/// <summary>UDP server on LiteNetLib. Also answers LAN discovery broadcasts (<see cref="BattleMessage.Discover"/>).</summary>
public sealed class LiteNetServerTransport : IServerTransport
{
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _manager;
    private readonly int _port;
    private readonly string _connectKey;
    private readonly Func<BitWriter, bool>? _roomInfo;
    private readonly BitWriter _discoveryWriter = new(256);
    private readonly NetDataWriter _reply = new();
    private ITransportHandler? _handler;

    /// <param name="connectKey">What a client must present to connect (<see cref="BattleProtocol.ConnectKey"/>).</param>
    /// <param name="roomInfo">Writes the reply to a discovery broadcast, or returns false for none; null: discovery is not answered.</param>
    public LiteNetServerTransport(int port, int maxConnections, string connectKey, Func<BitWriter, bool>? roomInfo = null)
    {
        _port = port;
        _connectKey = connectKey;
        _roomInfo = roomInfo;
        _manager = new NetManager(_listener)
        {
            ChannelsCount = 2,
            BroadcastReceiveEnabled = true,
            UnconnectedMessagesEnabled = true,
            AutoRecycle = true,
            DisconnectTimeout = 5000,
            UpdateTime = 1,
            EnableStatistics = true,
        };

        _listener.ConnectionRequestEvent += request =>
        {
            if (_manager.ConnectedPeersCount >= maxConnections) request.Reject();
            else request.AcceptIfKey(_connectKey);
        };
        _listener.PeerConnectedEvent += peer => _handler?.OnPeerConnected(peer.Id);
        _listener.PeerDisconnectedEvent += (peer, info) => _handler?.OnPeerDisconnected(peer.Id, info.Reason.ToString());
        _listener.NetworkReceiveEvent += (peer, reader, _, _) => _handler?.OnData(peer.Id, reader.GetRemainingBytesSpan());
        _listener.NetworkReceiveUnconnectedEvent += OnUnconnected;
    }

    public NetManager Manager => _manager;

    public void Start()
    {
        if (!_manager.Start(IPAddress.Any, IPAddress.IPv6Any, _port))
            throw new InvalidOperationException($"无法占用 UDP 端口 {_port}，可能已有另一个房间在运行。");
    }

    public void Poll(ITransportHandler handler)
    {
        _handler = handler;
        try
        {
            _manager.PollEvents();
        }
        finally
        {
            _handler = null;
        }
    }

    public void Send(int peerId, ReadOnlySpan<byte> data, Channel channel)
    {
        if (_manager.TryGetPeerById(peerId, out var peer) && peer is NetPeer netPeer)
            netPeer.Send(data, (byte)channel, ToDelivery(channel));
    }

    public void Disconnect(int peerId)
    {
        if (_manager.TryGetPeerById(peerId, out var peer)) _manager.DisconnectPeer(peer);
    }

    public TransportStats GetStats(int peerId) =>
        _manager.TryGetPeerById(peerId, out var peer)
            ? new TransportStats(peer.RoundTripTime, peer.Statistics.BytesSent, peer.Statistics.BytesReceived, peer.Statistics.PacketLossPercent)
            : default;

    public void Dispose() => _manager.Stop();

    internal static DeliveryMethod ToDelivery(Channel channel) =>
        channel == Channel.Reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;

    private void OnUnconnected(IPEndPoint remote, NetPacketReader reader, UnconnectedMessageType type)
    {
        if (_roomInfo is null) return;
        var data = reader.GetRemainingBytesSpan();
        try
        {
            var r = new BitReader(data);
            if (r.ReadByte() != (byte)BattleMessage.Discover) return;
            _discoveryWriter.Reset();
            if (!_roomInfo(_discoveryWriter)) return;
            _reply.Reset();
            _reply.Put(_discoveryWriter.Data);
            _manager.SendUnconnectedMessage(_reply, remote);
        }
        catch (ProtocolException)
        {
            // Not ours / malformed: ignore stray broadcast traffic.
        }
    }
}
