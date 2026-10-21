using Tank.Net.Protocol;
using Tank.Sim.Geometry;

namespace Tank.Net.Transport;

/// <summary>Link quality for one direction of a simulated connection.</summary>
public sealed record LinkConditions(double LatencyMs = 0, double JitterMs = 0, double LossPercent = 0)
{
    public static readonly LinkConditions Perfect = new();
}

/// <summary>
/// Deterministic in-memory network driven by a virtual clock: latency, jitter and loss are simulated
/// exactly the same way on every run, so netcode tests and soak runs are reproducible.
/// Reliable traffic is never lost and stays in order; unreliable traffic may be lost or reordered.
/// </summary>
public sealed class LoopbackNetwork
{
    /// <summary>Delivery order: arrival time, then send order (unique, so the unstable sort is deterministic).</summary>
    private static readonly Comparison<Packet> ByArrival = (a, b) =>
        a.ArrivalTime != b.ArrivalTime ? a.ArrivalTime.CompareTo(b.ArrivalTime) : a.Sequence.CompareTo(b.Sequence);

    private readonly List<Packet> _inFlight = new();
    private readonly List<Packet> _due = new();
    private XorShift _rng;
    private long _sequence;
    private bool _delivering;

    public LoopbackNetwork(uint seed = 1) => _rng = new XorShift(seed);

    public double Now { get; private set; }

    public LoopbackServerTransport? Server { get; private set; }

    public void AdvanceTime(double seconds) => Now += seconds;

    public LoopbackServerTransport CreateServer()
    {
        Server = new LoopbackServerTransport(this);
        return Server;
    }

    public LoopbackClientTransport Connect(LinkConditions? up = null, LinkConditions? down = null)
    {
        if (Server is null) throw new InvalidOperationException("Create the server first.");
        var client = new LoopbackClientTransport(this, Server.AllocatePeerId(), up ?? LinkConditions.Perfect, down ?? LinkConditions.Perfect);
        Server.Attach(client);
        return client;
    }

    internal void Enqueue(int fromPeer, int toPeer, bool toServer, ReadOnlySpan<byte> data, Channel channel, LinkConditions link, double lastReliableArrival, out double arrival)
    {
        arrival = Now + Delay(link);
        if (channel == Channel.Reliable)
        {
            arrival = System.Math.Max(arrival, lastReliableArrival); // keep ordered
        }
        else if (link.LossPercent > 0 && NextBelow(10_000) < link.LossPercent * 100)
        {
            return;
        }
        _inFlight.Add(new Packet(fromPeer, toPeer, toServer, data.ToArray(), arrival, _sequence++));
    }

    /// <summary>
    /// Hands this endpoint's due packets to <paramref name="deliver"/> by arrival time, then send order. Packets sent
    /// from inside <paramref name="deliver"/> wait for a later call. If <paramref name="deliver"/> throws, the packets
    /// after the one it threw on stay in flight. Not re-entrant.
    /// </summary>
    internal void Deliver(bool toServer, int peerId, Action<Packet> deliver)
    {
        if (_delivering) throw new InvalidOperationException("LoopbackNetwork.Deliver is not re-entrant.");
        _delivering = true;
        int handed = 0;
        try
        {
            int kept = 0;
            for (int i = 0; i < _inFlight.Count; i++)
            {
                Packet p = _inFlight[i];
                if (p.ToServer == toServer && (toServer || p.ToPeer == peerId) && p.ArrivalTime <= Now) _due.Add(p);
                else _inFlight[kept++] = p;
            }
            _inFlight.RemoveRange(kept, _inFlight.Count - kept);
            _due.Sort(ByArrival);
            while (handed < _due.Count) deliver(_due[handed++]);
        }
        finally
        {
            for (int i = handed; i < _due.Count; i++) _inFlight.Add(_due[i]);   // only after a throw
            _due.Clear();
            _delivering = false;
        }
    }

    private double Delay(LinkConditions link)
    {
        double jitter = link.JitterMs > 0 ? (NextBelow(10_001) / 10_000.0) * link.JitterMs : 0;
        return (link.LatencyMs + jitter) / 1000.0;
    }

    /// <summary>Uniform in [0, <paramref name="max"/>); the draws the xorshift32 generator has always made here, so seeded runs replay unchanged.</summary>
    private int NextBelow(int max) => (int)(_rng.NextUInt() % (uint)max);

    internal sealed record Packet(int FromPeer, int ToPeer, bool ToServer, byte[] Data, double ArrivalTime, long Sequence);
}

public sealed class LoopbackServerTransport : IServerTransport
{
    private readonly LoopbackNetwork _network;
    private readonly Dictionary<int, LoopbackClientTransport> _clients = new();
    private readonly Queue<(int Peer, bool Connected)> _connectionEvents = new();
    private readonly Dictionary<int, double> _lastReliable = new();
    private int _nextPeerId = 1;

    internal LoopbackServerTransport(LoopbackNetwork network) => _network = network;

    public long BytesSent { get; private set; }

    public void Start() { }

    internal int AllocatePeerId() => _nextPeerId++;

    internal void Attach(LoopbackClientTransport client)
    {
        _clients[client.PeerId] = client;
        _connectionEvents.Enqueue((client.PeerId, true));
    }

    internal void Detach(int peerId)
    {
        if (_clients.Remove(peerId)) _connectionEvents.Enqueue((peerId, false));
    }

    public void Poll(ITransportHandler handler)
    {
        while (_connectionEvents.Count > 0)
        {
            var (peer, connected) = _connectionEvents.Dequeue();
            if (connected) handler.OnPeerConnected(peer);
            else handler.OnPeerDisconnected(peer, "closed");
        }
        _network.Deliver(true, 0, p =>
        {
            if (_clients.ContainsKey(p.FromPeer)) handler.OnData(p.FromPeer, p.Data);
        });
    }

    public void Send(int peerId, ReadOnlySpan<byte> data, Channel channel)
    {
        if (!_clients.TryGetValue(peerId, out var client)) return;
        BytesSent += data.Length;
        _lastReliable.TryGetValue(peerId, out double last);
        _network.Enqueue(0, peerId, false, data, channel, client.Down, last, out double arrival);
        if (channel == Channel.Reliable) _lastReliable[peerId] = arrival;
    }

    public void Disconnect(int peerId)
    {
        if (_clients.TryGetValue(peerId, out var client)) client.Close("kicked");
        Detach(peerId);
    }

    public TransportStats GetStats(int peerId) =>
        _clients.TryGetValue(peerId, out var c) ? c.Stats : default;

    public void Dispose() { }
}

public sealed class LoopbackClientTransport : IClientTransport
{
    private readonly LoopbackNetwork _network;
    private bool _announced;
    private string? _closeReason;
    private double _lastReliable;
    private long _bytesSent, _bytesReceived;

    internal LoopbackClientTransport(LoopbackNetwork network, int peerId, LinkConditions up, LinkConditions down)
    {
        _network = network;
        PeerId = peerId;
        Up = up;
        Down = down;
    }

    public int PeerId { get; }
    public LinkConditions Up { get; set; }
    public LinkConditions Down { get; set; }
    public bool IsConnected => _closeReason is null;

    public TransportStats Stats => new((int)(Up.LatencyMs + Down.LatencyMs), _bytesSent, _bytesReceived, (float)Down.LossPercent);

    public void Poll(ITransportHandler handler)
    {
        if (!_announced)
        {
            _announced = true;
            handler.OnPeerConnected(0);
        }
        if (_closeReason is not null)
        {
            handler.OnPeerDisconnected(0, _closeReason);
            return;
        }
        _network.Deliver(false, PeerId, p =>
        {
            _bytesReceived += p.Data.Length;
            handler.OnData(0, p.Data);
        });
    }

    public void Send(ReadOnlySpan<byte> data, Channel channel)
    {
        if (!IsConnected) return;
        _bytesSent += data.Length;
        _network.Enqueue(PeerId, 0, true, data, channel, Up, _lastReliable, out double arrival);
        if (channel == Channel.Reliable) _lastReliable = arrival;
    }

    internal void Close(string reason) => _closeReason ??= reason;

    public void Dispose()
    {
        Close("closed");
        _network.Server?.Detach(PeerId);
    }
}
