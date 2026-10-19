using Tank.Net.Battle;
using Tank.Net.Protocol;
using Tank.Net.Transport;

namespace Tank.Net.Tests.Battle;

/// <summary>A packet the server sent, kept only while <see cref="FakeServerTransport.Capture"/> is on.</summary>
internal readonly record struct SentPacket(int PeerId, Channel Channel, byte[] Data)
{
    public BattleMessage Type => (BattleMessage)Data[0];
}

/// <summary>
/// An <see cref="IServerTransport"/> a test drives by hand: it injects peer connects, disconnects and packets (delivered
/// in order by the next <see cref="Poll"/>) and counts what the server sends per peer and message type. Counting
/// allocates nothing, so a test can measure the server's own allocations; with <see cref="Capture"/> on, every packet is
/// also copied into <see cref="Sent"/> for decoding (which does allocate).
/// </summary>
internal sealed class FakeServerTransport : IServerTransport
{
    private const int MaxPeers = 32;
    private const int MessageKinds = 64;
    private const int QueueSize = 4096;

    private enum EventKind : byte { Connect, Disconnect, Data }

    private readonly record struct Event(EventKind Kind, int PeerId, byte[]? Data);

    private readonly Event[] _queue = new Event[QueueSize];
    private int _head, _count;
    private readonly int[,] _sentCounts = new int[MaxPeers, MessageKinds];
    private readonly long[,] _sentBytes = new long[MaxPeers, MessageKinds];

    /// <summary>The peers the server asked to drop, in order (one entry per <see cref="Disconnect"/> call).</summary>
    public List<int> Kicked { get; } = new(64);

    /// <summary>The peers the test reported gone to the server, in order.</summary>
    public List<int> Disconnected { get; } = new(64);

    /// <summary>The packets the server sent, when <see cref="Capture"/> was on.</summary>
    public List<SentPacket> Sent { get; } = new();

    public bool Capture { get; set; }

    /// <summary>Like the real transports: a peer the server drops is reported gone on the next poll.</summary>
    public bool EchoKicks { get; set; }

    // ---- driving the server ----

    public void Connect(int peerId) => Enqueue(new Event(EventKind.Connect, peerId, null));

    public void Drop(int peerId) => Enqueue(new Event(EventKind.Disconnect, peerId, null));

    public void Receive(int peerId, byte[] data) => Enqueue(new Event(EventKind.Data, peerId, data));

    private void Enqueue(in Event e)
    {
        if (_count == QueueSize) throw new InvalidOperationException("The fake transport's queue is full.");
        _queue[(_head + _count++) % QueueSize] = e;
    }

    // ---- what the server sent ----

    public int CountTo(int peerId, BattleMessage type) => _sentCounts[peerId, (int)type];

    public long BytesTo(int peerId, BattleMessage type) => _sentBytes[peerId, (int)type];

    public IEnumerable<SentPacket> SentTo(int peerId, BattleMessage type) => Sent.Where(p => p.PeerId == peerId && p.Type == type);

    public SentPacket LastTo(int peerId, BattleMessage type) => SentTo(peerId, type).Last();

    // ---- IServerTransport ----

    public void Start() { }

    public void Poll(ITransportHandler handler)
    {
        int pending = _count;   // what a handler enqueues meanwhile (an echoed kick) is delivered by the next poll
        for (int i = 0; i < pending; i++)
        {
            Event e = _queue[_head];
            _head = (_head + 1) % QueueSize;
            _count--;
            switch (e.Kind)
            {
                case EventKind.Connect: handler.OnPeerConnected(e.PeerId); break;
                case EventKind.Disconnect:
                    Disconnected.Add(e.PeerId);
                    handler.OnPeerDisconnected(e.PeerId, "test");
                    break;
                default: handler.OnData(e.PeerId, e.Data!); break;
            }
        }
    }

    public void Send(int peerId, ReadOnlySpan<byte> data, Channel channel)
    {
        if (data.Length == 0) return;
        int kind = data[0] % MessageKinds;
        _sentCounts[peerId, kind]++;
        _sentBytes[peerId, kind] += data.Length;
        if (Capture) Sent.Add(new SentPacket(peerId, channel, data.ToArray()));
    }

    public void Disconnect(int peerId)
    {
        Kicked.Add(peerId);
        if (EchoKicks) Drop(peerId);
    }

    public TransportStats GetStats(int peerId) => default;

    public void Dispose() { }
}
