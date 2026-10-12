using Tank.Core;
using Tank.Core.Math;
using Tank.Core.World;
using Tank.Net.Protocol;

namespace Tank.Net.Client;

public sealed partial class GameClient
{
    private const int ChecksumMarginTicks = 30;
    private const int MaxObstacles = GameState.MaxTanks + GameState.MaxEagles;

    private readonly Queue<EventBatch> _pendingEvents = new();
    private readonly Box[] _obstacles = new Box[MaxObstacles];
    private readonly Box[] _hitTargets = new Box[GameState.MaxTanks];
    private int _mapTick;
    private int _predictionMapTick;
    private int _checkTick = -1;
    private uint _checkSum;
    private int _checksumFailures;
    private int _mapResyncs;
    private int _droppedPackets;

    // stats accumulators
    private double _statsTimer;
    private int _snapshotsThisSecond, _snapshotsPerSecond;
    private long _lastBytesIn, _lastBytesOut;
    private double _bytesInRate, _bytesOutRate;

    public MatchStartMessage? MatchInfo { get; private set; }
    public MapDefinition? MapDefinition { get; private set; }

    /// <summary>Terrain as of <see cref="SnapshotInterpolator.RenderTick"/> (destruction appears in sync with visuals).</summary>
    public GameMap? Map { get; private set; }

    /// <summary>
    /// Newest known terrain (changes applied the moment they arrive). Prediction uses this one: driving
    /// through a freshly blasted gap must not be blocked by bricks that are only still drawn for visual sync.
    /// </summary>
    public GameMap? PredictionMap { get; private set; }

    public Snapshot? LatestSnapshot => Store.Newest;

    /// <summary>Own tank position for rendering (sub units), with render interpolation and correction smoothing.</summary>
    public (double X, double Z) OwnTankRenderPosition()
    {
        var p = Predictor;
        double a = Clock.Alpha;
        return (p.Previous.X + (p.Current.X - p.Previous.X) * a + p.VisualOffsetX,
                p.Previous.Z + (p.Current.Z - p.Previous.Z) * a + p.VisualOffsetZ);
    }

    private void HandleMatchStart(MatchStartMessage msg)
    {
        // Same match number: the server is resyncing our map. Anything else is a new match, even if the
        // lobby message announcing the end of the previous one has not arrived yet.
        bool isResync = Phase == ClientPhase.InMatch && MatchInfo is { } current && current.MatchNumber == msg.MatchNumber;
        MatchInfo = msg;
        MapDefinition = MapParser.Parse(msg.MapName, msg.MapText);
        Map = MapDefinition.CreateMap();
        Map.LoadCells(msg.Cells);
        PredictionMap = Map.Clone();
        _mapTick = _predictionMapTick = msg.CurrentTick;
        _pendingEvents.Clear();
        _checkTick = -1;
        _checksumFailures = 0;
        if (isResync)
        {
            _mapResyncs++; // resync of a running match: keep clocks and prediction
            return;
        }
        Store.Clear();
        Interpolator.Reset();
        Clock.Reset();
        Predictor.Reset();
        _fxEvents.Clear();
        Phase = ClientPhase.InMatch;
    }

    private void HandleSnapshot(ref BitReader r)
    {
        if (Phase != ClientPhase.InMatch || Map is null) return;
        _codec.Read(ref r, _findSnapshot, _decodeScratch, out var header, out _);
        if (!Store.Add(_decodeScratch)) return;
        _snapshotsThisSecond++;

        double now = _clock();
        Interpolator.OnSnapshot(_decodeScratch.Tick, now);

        // Older (reordered) snapshots are kept as baselines/interpolation frames but carry stale feedback.
        if (_decodeScratch.Tick == Store.NewestTick)
        {
            Clock.OnServerFeedback(header.InputSlack, _decodeScratch.Tick, _transport.Stats.RttMs / 1000.0);
            var snap = Store.Newest!;
            int n = BuildObstacles(snap);
            Predictor.Reconcile(snap, header, Slot, PredictionMap!, _obstacles.AsSpan(0, n), _localEvents);
            FlushLocalEvents();
            if (snap.Tick % Units.TickRate == 0 && _checkTick < 0)
            {
                _checkTick = snap.Tick;
                _checkSum = snap.MapChecksum;
            }
        }
    }

    private void OnEventBatch(EventBatch batch)
    {
        if (PredictionMap is not null && batch.Tick > _predictionMapTick)
        {
            for (int i = 0; i < batch.Cells.Count; i++) PredictionMap.Apply(batch.Cells[i]);
            _predictionMapTick = batch.Tick;
        }
        _pendingEvents.Enqueue(batch);
    }

    private void SimulateTick(LocalInput input, GameMap map)
    {
        int tick = Clock.NextTick();
        int view = (int)System.Math.Round(tick - Interpolator.RenderTick);
        var newest = Store.Newest;
        // Once the match is over the server stops simulating; predicting movement would only produce corrections.
        bool ended = newest is not null && newest.Phase == MatchPhase.Ended;
        var cmd = ended
            ? InputCmd.Idle(tick)
            : new InputCmd(tick, InputCmd.Encode(input.Move), _fireLatched, (byte)System.Math.Clamp(view, 0, 15));
        _fireLatched = input.FireHeld;

        int obstacles = newest is null ? 0 : BuildObstacles(newest);
        int targets = BuildHitTargets();
        // A finished match is frozen: no movement and no leftover ice momentum.
        bool frozen = ended || (Predictor.HasTank && newest is not null && newest.TryFind(Predictor.Current.Id, out var me) && me.Has(EntityFlags.Frozen));
        Predictor.Step(cmd, map, _obstacles.AsSpan(0, obstacles), _hitTargets.AsSpan(0, targets), frozen, _localEvents);
        FlushLocalEvents();

        int count = Predictor.RecentInputs(_sendScratch);
        _writer.Reset();
        InputPacket.Write(_writer, Store.NewestTick, _sendScratch.AsSpan(0, count));
        _transport.Send(_writer.Data, Channel.Unreliable);
    }

    /// <summary>Everything our own tank can bump into, from the newest server state.</summary>
    private int BuildObstacles(Snapshot snap)
    {
        int n = 0;
        int ownId = (uint)Slot < (uint)snap.Players.Length ? snap.Players[Slot].TankId : 0;
        foreach (ref readonly var e in snap.EntitySpan)
        {
            if (n >= MaxObstacles) break;
            if (e.Kind == EntityKind.Tank && e.Id != ownId) _obstacles[n++] = new Box(e.X, e.Z, Units.TankSize, Units.TankSize);
            else if (e.Kind == EntityKind.Eagle && e.Has(EntityFlags.Alive)) _obstacles[n++] = new Box(e.X, e.Z, Units.Tile, Units.Tile);
        }
        return n;
    }

    /// <summary>Hostile tanks where we currently draw them: our predicted bullets stop on what we see.</summary>
    private int BuildHitTargets()
    {
        if (!Predictor.HasTank) return 0;
        int n = 0;
        byte myTeam = Predictor.Current.Team;
        foreach (var re in _entities)
        {
            var e = re.Entity;
            if (e.Kind != EntityKind.Tank || e.Team == myTeam || e.Has(EntityFlags.Spawning) || n >= _hitTargets.Length) continue;
            _hitTargets[n++] = new Box((int)re.X, (int)re.Z, Units.TankSize, Units.TankSize);
        }
        return n;
    }

    private void ApplyDueEvents()
    {
        if (Map is null) return;
        while (_pendingEvents.Count > 0 && _pendingEvents.Peek().Tick <= Interpolator.RenderTick)
        {
            var batch = _pendingEvents.Dequeue();
            if (batch.Tick <= _mapTick) continue; // already contained in the map we loaded
            for (int i = 0; i < batch.Cells.Count; i++) Map.Apply(batch.Cells[i]);
            _mapTick = batch.Tick;
            for (int i = 0; i < batch.Events.Count; i++)
            {
                var e = batch.Events[i];
                // Our own shots and their impacts were already shown by prediction.
                bool predictedLocally = (e.Type == GameEventType.Fired && e.A == Slot)
                                        || (e.Type == GameEventType.BulletSpark && e.B == Slot);
                if (!predictedLocally) _fxEvents.Enqueue(e);
            }
        }
    }

    private void FlushLocalEvents()
    {
        foreach (var e in _localEvents) _fxEvents.Enqueue(e);
        _localEvents.Clear();
    }

    /// <summary>
    /// Once our map has caught up to a tick we hold a server checksum for, compare. Two mismatches in a row
    /// (never expected) trigger a full map resync instead of letting walls silently diverge.
    /// </summary>
    private void CheckMapChecksum()
    {
        if (_checkTick < 0 || Map is null || Interpolator.RenderTick < _checkTick + ChecksumMarginTicks) return;
        bool mapAtCheckTick = _mapTick <= _checkTick && (_pendingEvents.Count == 0 || _pendingEvents.Peek().Tick > _checkTick);
        if (mapAtCheckTick)
        {
            if (Map.ComputeChecksum() == _checkSum) _checksumFailures = 0;
            else if (++_checksumFailures >= 2)
            {
                _writer.Reset();
                _writer.WriteByte((byte)MessageType.RequestResync);
                _transport.Send(_writer.Data, Channel.Reliable);
                _checksumFailures = 0;
            }
        }
        _checkTick = -1;
    }

    private void UpdateStats(double dt)
    {
        _statsTimer += dt;
        var t = _transport.Stats;
        if (_statsTimer >= 1.0)
        {
            _bytesInRate = (t.BytesReceived - _lastBytesIn) / _statsTimer;
            _bytesOutRate = (t.BytesSent - _lastBytesOut) / _statsTimer;
            _lastBytesIn = t.BytesReceived;
            _lastBytesOut = t.BytesSent;
            _snapshotsPerSecond = _snapshotsThisSecond;
            _snapshotsThisSecond = 0;
            _statsTimer = 0;
        }
        Stats = new NetStats(
            t.RttMs,
            Interpolator.JitterPeakSeconds * 1000,
            Interpolator.DelayTicks * 1000.0 / Units.TickRate,
            t.PacketLossPercent,
            _bytesInRate,
            _bytesOutRate,
            Interpolator.ExtrapolatedFrames,
            Predictor.Mispredictions,
            Clock.SmoothedSlack,
            Clock.Rate,
            _snapshotsPerSecond,
            _mapResyncs);
    }
}
