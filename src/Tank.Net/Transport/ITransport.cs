using Tank.Net.Protocol;

namespace Tank.Net.Transport;

/// <summary>Receives transport callbacks. Always invoked synchronously from inside <c>Poll</c>.</summary>
public interface ITransportHandler
{
    void OnPeerConnected(int peerId);

    void OnPeerDisconnected(int peerId, string reason);

    /// <summary>The span is only valid for the duration of the call.</summary>
    void OnData(int peerId, ReadOnlySpan<byte> data);
}

public readonly record struct TransportStats(int RttMs, long BytesSent, long BytesReceived, float PacketLossPercent);

public interface IServerTransport : IDisposable
{
    void Start();

    void Poll(ITransportHandler handler);

    void Send(int peerId, ReadOnlySpan<byte> data, Channel channel);

    void Disconnect(int peerId);

    TransportStats GetStats(int peerId);
}

public interface IClientTransport : IDisposable
{
    bool IsConnected { get; }

    /// <summary>Server is reported as peer 0.</summary>
    void Poll(ITransportHandler handler);

    void Send(ReadOnlySpan<byte> data, Channel channel);

    TransportStats Stats { get; }
}
