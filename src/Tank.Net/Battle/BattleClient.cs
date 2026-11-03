using Tank.Net.Protocol;
using Tank.Net.Timing;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Net.Battle;

/// <summary>
/// The battle client (design §5.9): connects, sits in the room, and in a match owns prediction, reconciliation,
/// interpolation, the shells and the prop state, exposing ready-to-draw state. Drive it with <see cref="Update"/> once
/// per rendered frame. Engine-free: tests and the soak run it as it is. Not thread-safe: every piece of state — the room,
/// the match, the snapshots, the zone (<see cref="HasZone"/>, <see cref="ZoneRings"/>, <see cref="ZoneAt"/>), the crates
/// (<see cref="Crates"/>, <see cref="TryDequeuePickup"/>) — is written in <see cref="OnData"/>, which the transport calls
/// from <see cref="Update"/>'s poll, and must be read on that same thread (in the game, the Godot main thread, which polls).
/// </summary>
public sealed partial class BattleClient : ITransportHandler, IDisposable
{
    private readonly IClientTransport _transport;
    private readonly Func<double> _clock;
    private readonly string _playerName;
    private readonly int _protocolVersion;
    private readonly BitWriter _writer = new(512);
    private readonly BattleSnapshotCodec _codec = new();
    private readonly BattleSnapshot _decodeScratch = new();
    private readonly InputCmd[] _sendScratch = new InputCmd[BattleProtocol.MaxRedundantInputs];
    private readonly List<RemoteTank> _others = new(BattleProtocol.MaxPlayers);
    private readonly Queue<GameEvent> _fxEvents = new();
    private readonly Func<int, BattleSnapshot?> _findSnapshot;
    private const double ResyncRequestInterval = 1.0;   // s
    private double _lastResyncRequest = double.NegativeInfinity;
    private bool _needMap;   // a match start came without the map we lack: keep asking for it (with the map) until it comes

    public BattleClient(IClientTransport transport, string playerName, Func<double> clock, uint sessionToken = 0)
        : this(transport, playerName, clock, sessionToken, BattleProtocol.Version) { }

    /// <summary>For tests: say hello with another protocol version.</summary>
    internal BattleClient(IClientTransport transport, string playerName, Func<double> clock, uint sessionToken, int protocolVersion)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _playerName = playerName;
        _protocolVersion = protocolVersion;
        SessionToken = sessionToken;
        Store = new BattleSnapshotStore();
        Interpolator = new BattleInterpolator(Store);
        Clock = new ClockSync(SimConstants.TickRate);
        _findSnapshot = Store.Find;
    }

    public BattlePhase Phase { get; private set; } = BattlePhase.Connecting;
    public string? DisconnectReason { get; private set; }
    public byte Slot { get; private set; } = MatchRules.NoSlot;
    public uint SessionToken { get; private set; }
    public RoomStateMessage? Room { get; private set; }
    public bool IsHost => Room is not null && Room.HostSlot == Slot;

    public BattleSnapshotStore Store { get; }
    public BattleInterpolator Interpolator { get; }
    public ClockSync Clock { get; }
    public BattlePredictor Predictor { get; } = new();

    /// <summary>The other tanks at render time, interpolated; the own tank is in <see cref="Predictor"/>.</summary>
    public IReadOnlyList<RemoteTank> Others => _others;

    public BattleStats Stats { get; private set; }

    /// <summary>Why the most recent packet was dropped, if any.</summary>
    public string? LastPacketError { get; private set; }

    /// <summary>How many times the match state was asked for again: after an undecodable snapshot, or for a map we lack.</summary>
    public int Resyncs { get; private set; }

    public void Update(double dt, in LocalCommand input)
    {
        _transport.Poll(this);
        if (_needMap) RequestMatchState();   // rate-limited: the answer to the last request may still be on its way
        if (Phase == BattlePhase.InMatch && Field is not null)
        {
            int ticks = Clock.Advance(dt);
            for (int i = 0; i < ticks; i++) SimulateTick(input);
            double now = _clock();
            Interpolator.Advance(dt, now);
            ApplyDueEvents();
            Predictor.Prune(Interpolator.RenderTick);   // after the due events: a confirmation that is due is applied before its shot is given up on
            Interpolator.Sample(_others, OwnTankId);
            Predictor.DecayVisualOffset(dt);
            RefreshShellViews();
        }
        UpdateStats(dt);
    }

    /// <summary>Presentation events (shots, bursts, hits) in the order they should play.</summary>
    public bool TryDequeueEvent(out GameEvent e) => _fxEvents.TryDequeue(out e);

    // ---- room commands ----

    public void SetMode(BattleMode mode) => SendRoom(RoomCommandKind.SetMode, ModeRules.Id(mode));
    public void SetTeam(byte team) => SendRoom(RoomCommandKind.SetTeam, team.ToString());
    public void SetTank(string tankId) => SendRoom(RoomCommandKind.SetTank, tankId);
    public void SetBots(int bots) => SendRoom(RoomCommandKind.SetBots, bots.ToString());
    public void SetReady(bool ready) => SendRoom(RoomCommandKind.SetReady, ready ? "1" : "0");
    public void StartMatch() => SendRoom(RoomCommandKind.StartMatch, string.Empty);
    public void ReturnToRoom() => SendRoom(RoomCommandKind.ReturnToRoom, string.Empty);

    /// <summary>Asks for another map (host only, in the room); <paramref name="mapId"/> is a <c>MapCatalog</c> id.</summary>
    public void SetMap(string mapId) => SendRoom(RoomCommandKind.SetMap, mapId);

    public void Dispose() => _transport.Dispose();

    // ---- transport callbacks ----

    public void OnPeerConnected(int peerId)
    {
        _writer.Reset();
        BattleMessages.Write(_writer, new HelloMessage(_protocolVersion, _playerName, SessionToken));
        _transport.Send(_writer.Data, Channel.Reliable);
    }

    public void OnPeerDisconnected(int peerId, string reason)
    {
        Phase = BattlePhase.Disconnected;
        _needMap = false;
        DisconnectReason ??= reason;
    }

    public void OnData(int peerId, ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;
        var type = (BattleMessage)data[0];
        try
        {
            var r = new BitReader(data);
            r.ReadByte();
            switch (type)
            {
                case BattleMessage.Welcome: HandleWelcome(BattleMessages.ReadWelcome(ref r)); break;
                case BattleMessage.Reject: DisconnectReason = BattleMessages.ReadReject(ref r).Reason; Phase = BattlePhase.Disconnected; break;
                case BattleMessage.RoomState: HandleRoom(BattleMessages.ReadRoomState(ref r)); break;
                case BattleMessage.MatchStart: HandleMatchStart(BattleMessages.ReadMatchStart(ref r)); break;
                case BattleMessage.Snapshot: HandleSnapshot(ref r, data.Length); break;
                case BattleMessage.Events: OnEventBatch(BattleMessages.ReadEvents(ref r)); break;
                case BattleMessage.Zone: HandleZone(BattleMessages.ReadZone(ref r)); break;
                case BattleMessage.Crate: HandleCrate(BattleMessages.ReadCrate(ref r)); break;
            }
        }
        catch (ProtocolException ex)
        {
            // A malformed packet is dropped; the game keeps running and the reason is kept for diagnostics. Anything else
            // that escapes is a bug of ours, not of the packet, and must not be hidden here.
            _droppedPackets++;
            LastPacketError = ex.Message;
            if ((type == BattleMessage.Snapshot && Phase == BattlePhase.InMatch) || (type == BattleMessage.MatchStart && _needMap)) RequestMatchState();
        }
    }

    /// <summary>
    /// A snapshot we can't make sense of (a baseline we never got, an entity we don't know) means we have fallen out of
    /// step: ask the server to send the match state again, at most once a second (it also rate-limits). The server leaves
    /// the map out for a connection it has sent it to; a client that lacks it anyway (it got a match start without the map
    /// for a match it has no field for) says so, and keeps asking until a match start with the map arrives.
    /// </summary>
    private void RequestMatchState()
    {
        double now = _clock();
        if (now - _lastResyncRequest < ResyncRequestInterval) return;
        _lastResyncRequest = now;
        _writer.Reset();
        BattleMessages.Write(_writer, new MatchStateRequest(_needMap));
        _transport.Send(_writer.Data, Channel.Reliable);
        Resyncs++;
    }

    private void HandleWelcome(WelcomeMessage welcome)
    {
        Slot = welcome.Slot;
        SessionToken = welcome.SessionToken;
        if (Phase == BattlePhase.Connecting) Phase = BattlePhase.Room;
    }

    private void HandleRoom(RoomStateMessage room)
    {
        Room = room;
        if (room.MatchRunning) return;
        _needMap = false;   // no match whose map we could still want
        if (Phase == BattlePhase.InMatch) LeaveMatch();
    }

    private void SendRoom(RoomCommandKind kind, string arg)
    {
        _writer.Reset();
        BattleMessages.Write(_writer, new RoomCommandMessage(kind, arg));
        _transport.Send(_writer.Data, Channel.Reliable);
    }
}
