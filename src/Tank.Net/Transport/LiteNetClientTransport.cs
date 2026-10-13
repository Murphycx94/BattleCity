using LiteNetLib;
using Tank.Net.Protocol;

namespace Tank.Net.Transport;

public sealed class LiteNetClientTransport : IClientTransport
{
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _manager;
    private NetPeer? _server;
    private ITransportHandler? _handler;

    public LiteNetClientTransport(string host, int port)
    {
        _manager = new NetManager(_listener)
        {
            ChannelsCount = 2,
            AutoRecycle = true,
            DisconnectTimeout = 5000,
            UpdateTime = 1,
            EnableStatistics = true,
        };
        _listener.PeerConnectedEvent += peer =>
        {
            _server = peer;
            _handler?.OnPeerConnected(0);
        };
        _listener.PeerDisconnectedEvent += (_, info) =>
        {
            _server = null;
            _handler?.OnPeerDisconnected(0, info.Reason.ToString());
        };
        _listener.NetworkReceiveEvent += (_, reader, _, _) => _handler?.OnData(0, reader.GetRemainingBytesSpan());

        if (!_manager.Start()) throw new InvalidOperationException("无法打开 UDP 套接字。");
        _manager.Connect(host, port, ProtocolInfo.ConnectKey);
    }

    public NetManager Manager => _manager;

    public bool IsConnected => _server is { ConnectionState: ConnectionState.Connected };

    public TransportStats Stats => _server is null
        ? default
        : new TransportStats(_server.RoundTripTime, _server.Statistics.BytesSent, _server.Statistics.BytesReceived, _server.Statistics.PacketLossPercent);

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

    public void Send(ReadOnlySpan<byte> data, Channel channel) =>
        _server?.Send(data, (byte)channel, LiteNetServerTransport.ToDelivery(channel));

    public void Dispose() => _manager.Stop();
}
