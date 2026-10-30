using System.Numerics;
using Tank.Net.Protocol;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Net.Battle;

public sealed partial class BattleClient
{
    /// <summary>Event batches waiting for the render clock; a client that can't keep up drops the oldest rather than grow.</summary>
    private const int MaxPendingEventBatches = 256;

    private static readonly MatchBlock NoMatch = new() { WinnerTeam = MatchRules.NoTeam, WinnerSlot = MatchRules.NoSlot };

    private readonly Queue<EventBatch> _pendingEvents = new();
    private readonly List<RemoteShell> _remoteShells = new(16);
    private readonly List<ShellView> _shellViews = new(16);
    private readonly Obb2[] _obstacles = new Obb2[Simulation.MaxTanks];
    private readonly ZoneRing[] _zoneRings = new ZoneRing[BattleMessages.MaxZoneRings];
    private int _zoneRingCount;
    private int _droppedPackets;

    // stats accumulators
    private double _statsTimer;
    private int _snapshotsThisSecond, _snapshotsPerSecond, _lastSnapshotBytes;
    private long _lastBytesIn, _lastBytesOut;
    private double _bytesInRate, _bytesOutRate;

    /// <summary>Another tank's shell, flown here from its shot event (design §5.7); the server's events say what it struck.</summary>
    private struct RemoteShell
    {
        public Shell Shell;
        public Vector3 Previous;
        public bool Ended;
    }

    public MatchStartMessage? MatchInfo { get; private set; }

    /// <summary>The client's battlefield: built from the server's map, its props kept as the server reports them.</summary>
    public Battlefield? Field { get; private set; }

    public int OwnTankId { get; private set; }
    public BattleSnapshot? Latest => Store.Newest;

    /// <summary>Whether this match has a zone: its match start carried one (a royale).</summary>
    public bool HasZone => _zoneRingCount > 0;

    /// <summary>The simulation tick the match started at, which the zone's clock counts from (0 without a zone).</summary>
    public int ZoneStartTick { get; private set; }

    /// <summary>The rings known so far: ring 0, then ring 1 up to the last announced; empty without a zone.</summary>
    public ReadOnlySpan<ZoneRing> ZoneRings => _zoneRings.AsSpan(0, _zoneRingCount);

    /// <summary>
    /// The zone at server tick <paramref name="serverTick"/> (fractional: the interpolator's render tick) as far as the
    /// rings known go (<see cref="ZoneState.At"/>); Phase None without a zone. For drawing and the HUD only: the server
    /// alone deals the damage. No allocation.
    /// </summary>
    public ZoneState ZoneAt(double serverTick) =>
        HasZone ? ZoneState.At(ZoneRings, (float)(serverTick - ZoneStartTick)) : default;

    /// <summary>The match as the newest snapshot shows it (no winner, nothing scored, before the first one).</summary>
    public MatchBlock Match => Latest?.Match ?? NoMatch;

    public bool Spotted => Match.Spotted;

    /// <summary>The own tank as predicted, where to draw it (prediction plus the correction being smoothed away), and the blend between ticks.</summary>
    public bool HasTank => Predictor.HasTank;
    public TankState Own => Predictor.Current;
    public Vector2 OwnRenderPosition => Vector2.Lerp(Predictor.Previous.Position, Predictor.Current.Position, (float)Clock.Alpha) + Predictor.VisualOffset;

    /// <summary>
    /// Every shell in flight as of the last <see cref="Update"/>: the own predicted ones and the others' simulated ones.
    /// Refilled once per update, so reading it costs nothing and changes nothing; the list is reused, copy what must outlive the frame.
    /// </summary>
    public IReadOnlyList<ShellView> Shells => _shellViews;

    private void RefreshShellViews()
    {
        _shellViews.Clear();
        IReadOnlyList<PredictedShell> own = Predictor.Shells;
        for (int i = 0; i < own.Count; i++)
        {
            PredictedShell s = own[i];
            if (!s.Ended) _shellViews.Add(new ShellView(s.Shell.Id, s.Shell.OwnerTankId, s.Previous, s.Shell.Position, s.Shell.Velocity, s.Shell.Ammo, true, s.Shell.Age));
        }
        foreach (RemoteShell s in _remoteShells)
            if (!s.Ended) _shellViews.Add(new ShellView(s.Shell.Id, s.Shell.OwnerTankId, s.Previous, s.Shell.Position, s.Shell.Velocity, s.Shell.Ammo, false, s.Shell.Age));
    }

    private void HandleMatchStart(MatchStartMessage msg)
    {
        // Build and check everything before touching any state: a bad map or prop id must leave the match as it was.
        bool sameMatch = Phase == BattlePhase.InMatch && MatchInfo is { } current && current.MatchNumber == msg.MatchNumber;
        Battlefield field;
        if (msg.Map is { } map) field = BuildField(map);
        else if (sameMatch && Field is { } kept) field = kept.Fresh();   // a resync without the map: ours, every prop as new, as Build() would give
        else
        {
            // The server thinks this connection has the map, and we don't: drop it and ask for the state with the map.
            _needMap = true;
            throw new ProtocolException($"A match start without the map for match {msg.MatchNumber}, which we have no map of.");
        }
        ApplyProps(field, msg.Props);

        // Same match number (and not a gap of many seconds either way): the server is resyncing us (a reconnect), the store stays.
        // Anything else is a new match.
        bool resync = sameMatch && (Store.NewestTick < 0 || System.Math.Abs((long)msg.CurrentTick - Store.NewestTick) <= BattleProtocol.MaxTickLead);
        MatchInfo = msg.Map is null ? msg with { Map = MatchInfo!.Map } : msg;   // MatchInfo always carries the map
        Field = field;
        _needMap = false;
        OwnTankId = msg.MyTankId;
        SetZone(ModeRules.HasZone(msg.Mode) ? msg.Zone : null);   // a zone in another mode's start is no zone of ours
        _pendingEvents.Clear();
        _remoteShells.Clear();
        if (resync) return;
        Store.Clear();
        Interpolator.Reset();
        Clock.Reset();
        Predictor.Reset();
        _fxEvents.Clear();
        _shellViews.Clear();
        Phase = BattlePhase.InMatch;
    }

    private void LeaveMatch()
    {
        Phase = BattlePhase.Room;
        _needMap = false;   // no match to ask about
        MatchInfo = null;
        Field = null;
        OwnTankId = 0;
        SetZone(null);
        _pendingEvents.Clear();
        _remoteShells.Clear();
        _shellViews.Clear();
        Predictor.Reset();
        _others.Clear();
    }

    /// <summary>A match start's zone replaces whatever was known (a new match, or a resync of this one); null: none.</summary>
    private void SetZone(ZoneSync? zone)
    {
        _zoneRingCount = 0;
        ZoneStartTick = 0;
        if (zone is null) return;
        int count = System.Math.Min(zone.Rings.Count, _zoneRings.Length);   // the reader allows at most that many
        for (int k = 0; k < count; k++) _zoneRings[k] = zone.Rings[k];
        _zoneRingCount = count;
        ZoneStartTick = zone.StartTick;
    }

    /// <summary>
    /// A ring announced: appended when it is the next one of this match. Another match's, one already known (sent again),
    /// or one past a gap (the ring before it never came: a resync will bring them all) changes nothing. Reliable and in
    /// order, so only a match start racing an announcement leaves either of the last two.
    /// </summary>
    private void HandleZone(ZoneMessage msg)
    {
        if (Phase != BattlePhase.InMatch || MatchInfo is null || msg.MatchNumber != MatchInfo.MatchNumber || !HasZone) return;
        if (msg.Ring != _zoneRingCount || _zoneRingCount >= _zoneRings.Length) return;
        _zoneRings[_zoneRingCount++] = msg.Circle;
    }

    private static Battlefield BuildField(MapData map)
    {
        try
        {
            return map.Build();
        }
        catch (ArgumentException ex)
        {
            throw new ProtocolException($"The map can't be built: {ex.Message}");
        }
    }

    /// <summary>The tick the server's newest word is about: the newest snapshot's, or the match start's before the first one.</summary>
    private int ServerTickNow => Store.NewestTick >= 0 ? Store.NewestTick : MatchInfo?.CurrentTick ?? 0;

    private void CheckNotFarAhead(int tick, string what)
    {
        if ((long)tick - ServerTickNow > BattleProtocol.MaxTickLead) throw new ProtocolException($"{what} is from the far future (tick {tick}).");
    }

    private void HandleSnapshot(ref BitReader r, int bytes)
    {
        if (Phase != BattlePhase.InMatch || Field is null) return;
        _codec.Read(ref r, _findSnapshot, _decodeScratch, out ClientFeedback feedback, out _);
        if (feedback.LastProcessedInputTick > _decodeScratch.Tick) throw new ProtocolException("Inputs processed beyond the snapshot's own tick.");   // the server acks the tick it sends
        CheckNotFarAhead(_decodeScratch.Tick, "The snapshot");   // accepted, it would make every real one after it "too old"
        if (_decodeScratch.Tick == Store.NewestTick) return;     // a duplicate: neither a new sample for the clocks nor worth reconciling twice
        if (!Store.Add(_decodeScratch)) return;
        _snapshotsThisSecond++;
        _lastSnapshotBytes = bytes;
        double now = _clock();
        Interpolator.OnSnapshot(_decodeScratch.Tick, now);
        if (_decodeScratch.Tick != Store.NewestTick) return;   // reordered: a baseline and an interpolation frame, but stale feedback

        BattleSnapshot snap = Store.Newest!;
        Clock.OnServerFeedback(feedback.InputSlack, snap.Tick, _transport.Stats.RttMs / 1000.0);
        int n = BuildObstacles(snap);
        TankState? own = OwnState(snap);
        Predictor.Reconcile(own, feedback.LastProcessedInputTick, snap.Match.Phase == MatchPhase.Ended, Field, _obstacles.AsSpan(0, n));
        if (own is not null) OwnTankId = snap.Own.Id;   // an own id without its entity is a lie; the interpolator must not hide that tank
    }

    /// <summary>The own tank as the server has it, with the spec and team its entity carries.</summary>
    private static TankState? OwnState(BattleSnapshot snap)
    {
        if (!snap.HasOwn || !snap.TryFind(snap.Own.Id, out BattleEntity e)) return null;
        OwnTank o = snap.Own;
        return new TankState
        {
            Id = o.Id, Spec = TankCatalog.All[e.Spec], Team = e.Team, Position = new Vector2(o.X, o.Z), Yaw = o.Yaw, Speed = o.Speed,
            TurretYaw = o.TurretYaw, GunPitch = o.GunPitch, Reload = o.Reload, ShotCount = o.ShotCount, LastFired = o.LastFired,
            Health = o.Health, IsDestroyed = o.Destroyed, ProtectedUntil = o.ProtectedUntil, Ammo = o.Ammo, ApLeft = o.ApLeft, HeLeft = o.HeLeft, Bloom = o.Bloom,
        };
    }

    private void OnEventBatch(EventBatch batch)
    {
        if (Phase != BattlePhase.InMatch || Field is null) return;   // nothing plays them outside a match
        CheckNotFarAhead(batch.Tick, "The event batch");             // one at the head of the queue would hold every later one back
        ApplyProps(Field, batch.Props);                               // the newest prop state at once: prediction drives on it
        while (_pendingEvents.Count >= MaxPendingEventBatches)
        {
            _pendingEvents.Dequeue();
            _droppedPackets++;
        }
        _pendingEvents.Enqueue(batch);
    }

    /// <summary>Sets the props' hit points as the server reports them: all of them, or (an id the map doesn't have) none, with a <see cref="ProtocolException"/>.</summary>
    private static void ApplyProps(Battlefield field, IReadOnlyList<PropState> props)
    {
        for (int i = 0; i < props.Count; i++)
            if (!field.HasProp(props[i].PropId)) throw new ProtocolException($"Unknown prop {props[i].PropId}.");
        for (int i = 0; i < props.Count; i++)
        {
            PropState p = props[i];
            Prop prop = field.PropById(p.PropId);
            if (prop.IsDestructible) field.SetDurability(p.PropId, BattleProtocol.DequantizeDurability(p.Durability, prop.MaxDurability));
        }
    }

    private void SimulateTick(in LocalCommand input)
    {
        int tick = Clock.NextTick();
        bool ended = Latest is { Match.Phase: MatchPhase.Ended };
        var (yaw, pitch) = BattleProtocol.QuantizedAim(input.AimYaw, input.AimPitch);
        InputCmd cmd = ended ? BattleInputBuffer.Idle(tick, new InputCmd(tick, 0, 0, yaw, pitch, false, input.Ammo))
            : new InputCmd(tick, input.Throttle, input.Steer, yaw, pitch, input.Fire, input.Ammo);
        int n = Latest is null ? 0 : BuildObstacles(Latest);
        Predictor.Step(cmd, Field!, _obstacles.AsSpan(0, n));
        StepRemoteShells();

        int count = Predictor.RecentInputs(_sendScratch);
        _writer.Reset();
        BattleInputPacket.Write(_writer, Store.NewestTick, _sendScratch.AsSpan(0, count));
        _transport.Send(_writer.Data, Channel.Unreliable);
    }

    /// <summary>The other tanks' footprints, as the newest snapshot has them: what the own tank bumps into.</summary>
    private int BuildObstacles(BattleSnapshot snap)
    {
        int n = 0;
        foreach (ref readonly BattleEntity e in snap.EntitySpan)
        {
            if (e.Id == OwnTankId || (e.Id == snap.Own.Id && snap.HasOwn)) continue;
            TankSpec spec = TankCatalog.All[e.Spec];
            var at = new Vector2(BattleProtocol.FromCm(e.X), BattleProtocol.FromCm(e.Z));
            _obstacles[n++] = TankMotion.Footprint(spec, at, BattleProtocol.DequantizeAngle(e.Yaw, BattleProtocol.YawBits));
        }
        return n;
    }

    private void StepRemoteShells()
    {
        for (int i = _remoteShells.Count - 1; i >= 0; i--)
        {
            RemoteShell s = _remoteShells[i];
            if (s.Ended)
            {
                _remoteShells.RemoveAt(i);
                continue;
            }
            s.Previous = s.Shell.Position;
            if (Ballistics.Step(ref s.Shell, Field!, out _) != ShellStep.Flying) s.Ended = true;
            _remoteShells[i] = s;
        }
    }

    /// <summary>Plays the events whose tick the render clock has reached: shots become shells, bursts end them, the rest go to the presentation.</summary>
    private void ApplyDueEvents()
    {
        while (_pendingEvents.Count > 0 && _pendingEvents.Peek().Tick <= Interpolator.RenderTick)
        {
            EventBatch batch = _pendingEvents.Dequeue();
            for (int i = 0; i < batch.Events.Count; i++) ApplyEvent(batch.Events[i]);
        }
    }

    private void ApplyEvent(in GameEvent e)
    {
        // Our own shot the server reports: if we predicted it, it is already on screen (confirmed, with the server's id).
        bool predictedShot = e.Type == GameEventType.ShellFired && e.TankId == OwnTankId && Predictor.Confirm(e.Tick, e.ShellId);
        if (predictedShot) return;
        switch (e.Type)
        {
            case GameEventType.ShellFired:
                _remoteShells.Add(new RemoteShell
                {
                    Shell = new Shell { Id = e.ShellId, OwnerTankId = e.TankId, Position = e.Position, Velocity = e.Velocity, Ammo = e.Ammo },
                    Previous = e.Position,
                });
                break;
            case GameEventType.ShellExploded:
            case GameEventType.TankHit:
                EndShell(e.ShellId);
                break;
        }
        _fxEvents.Enqueue(e);
    }

    private void EndShell(int shellId)
    {
        Predictor.End(shellId);
        for (int i = 0; i < _remoteShells.Count; i++)
        {
            if (_remoteShells[i].Shell.Id != shellId) continue;
            RemoteShell s = _remoteShells[i];
            s.Ended = true;
            _remoteShells[i] = s;
        }
    }

    private void UpdateStats(double dt)
    {
        _statsTimer += dt;
        TransportStats t = _transport.Stats;
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
        Stats = new BattleStats(t.RttMs, Interpolator.JitterPeakSeconds * 1000, Interpolator.DelayTicks * 1000.0 / SimConstants.TickRate, t.PacketLossPercent,
            _bytesInRate, _bytesOutRate, _snapshotsPerSecond, _lastSnapshotBytes, Interpolator.ExtrapolatedFrames,
            Predictor.Corrections, Predictor.LastCorrection, Predictor.MeanCorrection, Predictor.MaxCorrection, Clock.SmoothedSlack, Clock.Rate, _droppedPackets);
    }
}
