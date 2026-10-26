using Tank.Net.Protocol;
using Tank.Sim;
using Tank.Sim.Ai;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Net.Battle;

public sealed partial class BattleServer
{
    private const int BotSeedStride = 7919;   // a prime: each bot's seed differs from the match seed in its own way

    /// <summary>
    /// Starts a match in the room's mode on the room's map with everyone seated, plus the bots asked for (named, given
    /// random tanks, put on the thinner team), if the map is ready (and, for a standard battle, has bases), there are at
    /// least two tanks and, in a team mode, both teams have one.
    /// </summary>
    public bool TryStartMatch(out string? reason)
    {
        if (_sim is not null)
        {
            reason = "a match is running";
            return false;
        }
        switch (_maps.StateOf(_roomMap))
        {
            case MapState.Generating:
                reason = "the map is still being generated";
                return false;
            case MapState.Failed:
                reason = "the map failed to generate";
                return false;
        }
        if (ModeRules.HasBases(_mode) && _roomMap.Bases.Count == 0)   // before the bots are seated: nothing to undo
        {
            reason = "the map has no bases";
            return false;
        }
        uint seed = _options.Seed ?? (uint)Random.Shared.Next(1, int.MaxValue);
        var rng = new Tank.Sim.Geometry.XorShift(seed);
        int humans = _seats.Count(s => s is { IsBot: false });
        for (int i = 0; i < _bots && humans + i < BattleProtocol.MaxPlayers; i++)
        {
            byte slot = (byte)Array.FindIndex(_seats, s => s is null);
            var bot = new BattleSeat(slot, 0, isBot: true) { Name = $"机器人 {i + 1}", TankIndex = (byte)(rng.NextUInt() % (uint)TankCatalog.All.Count) };
            bot.Team = ThinnerTeam(slot);
            _seats[slot] = bot;
        }
        int seated = _seats.Count(s => s is not null);
        bool teamsManned = !ModeRules.HasTeams(_mode) || (_seats.Any(s => s is { Team: 0 }) && _seats.Any(s => s is { Team: 1 }));
        if (seated < ModeRules.MinPlayers || !teamsManned)
        {
            RemoveBots();
            reason = seated < ModeRules.MinPlayers ? "fewer than two tanks" : "a team is empty";
            return false;
        }

        PreparedMap map = _maps.Get(_roomMap);
        _matchMap = map;
        // A Fresh() copy of the battlefield ServerMaps built while preparing the map (bit for bit map.Data.Build(), what
        // every client builds): about 0.1 ms even for the riverland, so the tick thread never builds a map.
        _sim = new Simulation(map.Definition.NewBattlefield());
        _vision = new VisionTracker(_sim);
        _rules = new MatchRules(_mode, _sim, map.Definition.Spawns, _options.MatchSeconds is { } seconds ? System.Math.Max(1, (int)(seconds * SimConstants.TickRate)) : null,
            bases: map.Definition.Bases);
        _matchNumber++;
        _endedAtFrame = -1;
        _hadTank.Clear();
        foreach (BattleSeat? seat in _seats)
        {
            if (seat is null) continue;
            PutIntoMatch(seat);
            if (seat.IsBot) seat.Bot = new SkirmishBot(_rules.Of(seat.Slot).TankId, map.Definition.Waypoints, seed ^ (uint)(seat.Slot * BotSeedStride));
            else seat.ResetForMatch();
        }
        _log($"match started: {ModeRules.Id(_mode)}, map {map.Definition.Id}, {humans} players, {seated - humans} bots, seed {seed}");
        foreach (BattleSeat seat in _byPeer.Values) SendMatchStart(seat, withMap: true);
        _roomDirty = true;
        reason = null;
        return true;
    }

    private void PutIntoMatch(BattleSeat seat)
    {
        _rules!.Join(seat.Slot, TankCatalog.All[seat.TankIndex], seat.Team);
        seat.InMatch = true;
        if (!seat.IsBot && !ModeRules.Respawns(_mode)) _hadTank.Add(seat.Token);
    }

    /// <summary>
    /// Whether a player who isn't in the match may be put into it now: it takes joins, and — in a mode without respawns
    /// — the player's token hasn't had a tank in it yet. Leaving counts as going out there (<see cref="MatchRules.Leave"/>),
    /// so a player who comes back, eliminated or not, after the seat was freed (quit to the menu, or away past the
    /// reconnect grace) waits in the room for the next match instead of taking a fresh tank. One still within the grace
    /// period never gets here: the seat, still in the match, hands back the tank it has, wreck or not.
    /// </summary>
    private bool MayJoinMatch(BattleSeat seat) => _rules!.AcceptsJoins && !_hadTank.Contains(seat.Token);

    public void EndMatch()
    {
        if (_sim is null) return;
        _log("match over, back to the room");
        _sim = null;
        _vision = null;
        _rules = null;
        _matchMap = null;
        _endedAtFrame = -1;
        _hadTank.Clear();
        RemoveBots();
        for (byte slot = 0; slot < _seats.Length; slot++)
        {
            BattleSeat? seat = _seats[slot];
            if (seat is null) continue;
            seat.InMatch = false;
            if (!seat.Connected) _seats[slot] = null;
            else seat.Ready = false;
        }
        _roomDirty = true;
    }

    private void RemoveBots()
    {
        for (int i = 0; i < _seats.Length; i++)
            if (_seats[i] is { IsBot: true }) _seats[i] = null;
    }

    private void TickMatch()
    {
        Simulation sim = _sim!;
        VisionTracker vision = _vision!;
        MatchRules rules = _rules!;
        int tick = sim.Tick + 1;

        // Inputs, one per tank in slot order: players from their buffers, bots from their brains.
        ReadOnlySpan<TankState> tanks = sim.Tanks;
        for (int i = 0; i < tanks.Length; i++)
        {
            BattleSeat? seat = SeatOfTank(tanks[i].Id);
            _tickInputs[i] = seat is null ? BattleInputBuffer.Idle(tick, default)
                : seat.IsBot ? seat.Bot!.Command(sim, vision, tick)
                : seat.Inputs.Take(tick);
        }
        sim.Step(_tickInputs.AsSpan(0, tanks.Length));
        vision.Update();
        rules.Update();

        CollectPropChanges(sim);
        foreach (BattleSeat seat in _byPeer.Values)
        {
            if (!seat.InMatch) continue;
            SendEvents(seat, sim, vision);
            SendSnapshot(seat, sim, vision, rules);
        }

        if (rules.Phase == MatchPhase.Ended)
        {
            if (_endedAtFrame < 0)
            {
                _endedAtFrame = _frame;
                _log($"match ended: {ModeRules.Id(_mode)}, {WinnerText(rules)}, {ReasonText(rules.EndReason)}");
            }
            if (_frame - _endedAtFrame >= _options.ReturnToRoomSeconds * SimConstants.TickRate) EndMatch();
        }
        else if (!AnyHumanInMatch())   // in the match, connected or not: a dropped player has the grace period to come back
        {
            _log("nobody left in the match");
            EndMatch();
        }
    }

    /// <summary>The winner for the log: "team N", "slot N" (a free-for-all) or "draw".</summary>
    private static string WinnerText(MatchRules rules) =>
        rules.WinnerTeam != MatchRules.NoTeam && ModeRules.HasTeams(rules.Mode) ? $"team {rules.WinnerTeam}"
        : rules.WinnerSlot != MatchRules.NoSlot ? $"slot {rules.WinnerSlot}"
        : "draw";

    private static string ReasonText(MatchEndReason reason) => reason switch
    {
        MatchEndReason.TimeUp => "time up",
        MatchEndReason.Eliminated => "eliminated",
        MatchEndReason.Captured => "captured",
        _ => $"reason {(byte)reason}",
    };

    /// <summary>
    /// Whether a player is in the match: connected, or away within the reconnect grace period (the seat, still in the
    /// match, is held). One waiting in the room for the next match (turned away from this one) doesn't keep it going.
    /// </summary>
    private bool AnyHumanInMatch()
    {
        foreach (BattleSeat? seat in _seats)
            if (seat is { IsBot: false, InMatch: true }) return true;
        return false;
    }

    private BattleSeat? SeatOfTank(int tankId)
    {
        foreach (BattleSeat? seat in _seats)
            if (seat is { InMatch: true } && _rules!.Of(seat.Slot).TankId == tankId) return seat;
        return null;
    }

    private Viewer ViewerOf(BattleSeat seat, Simulation sim)
    {
        ref readonly Standing standing = ref _rules!.Of(seat.Slot);
        int slot = sim.SlotOf(standing.TankId);
        bool destroyed = slot < 0 || sim.Tanks[slot].IsDestroyed;
        return new Viewer(standing.Team, standing.Active ? standing.TankId : 0, destroyed, _mode == BattleMode.FreeForAll);
    }

    /// <summary>The hit points, quantised, of every prop this tick's events touched, each once.</summary>
    private void CollectPropChanges(Simulation sim)
    {
        _propChanges.Clear();
        foreach (ref readonly GameEvent e in sim.Events)
        {
            if (e.Type is not (GameEventType.PropDamaged or GameEventType.PropDestroyed)) continue;
            bool known = false;
            foreach (PropState p in _propChanges) known |= p.PropId == e.PropId;
            if (known) continue;
            Prop prop = sim.Field.PropById(e.PropId);
            _propChanges.Add(new PropState(e.PropId, BattleProtocol.QuantizeDurability(sim.Field.Durability(e.PropId), prop.MaxDurability)));
        }
    }

    private void SendEvents(BattleSeat seat, Simulation sim, VisionTracker vision)
    {
        if (sim.Events.Length == 0) return;
        Viewer viewer = ViewerOf(seat, sim);
        _sentEvents.Clear();
        foreach (ref readonly GameEvent e in sim.Events)
            if (viewer.Wants(e, sim, vision, out GameEvent sent)) _sentEvents.Add(sent);
        if (_sentEvents.Count == 0 && _propChanges.Count == 0) return;
        _writer.Reset();
        BattleMessages.Write(_writer, new EventBatch(sim.Tick, _sentEvents, _propChanges));
        Send(seat, _writer.Data, Channel.Reliable);
    }

    private void SendSnapshot(BattleSeat seat, Simulation sim, VisionTracker vision, MatchRules rules)
    {
        int tick = sim.Tick;
        BattleSnapshot snapshot = seat.History![tick % BattleSeat.HistorySize];
        Build(snapshot, seat, sim, vision, rules);
        int ack = seat.AckedSnapshotTick;
        BattleSnapshot? baseline = null;
        if (ack > 0 && ack < tick && tick - ack < BattleSeat.HistorySize && seat.History[ack % BattleSeat.HistorySize].Tick == ack)
            baseline = seat.History[ack % BattleSeat.HistorySize];
        var feedback = new ClientFeedback { LastProcessedInputTick = tick, InputSlack = seat.Inputs.SlackAt(tick) };
        _writer.Reset();
        _codec.Write(_writer, snapshot, baseline, feedback);
        Send(seat, _writer.Data, ChannelFor(_writer.ByteLength));
    }

    /// <summary>What <paramref name="seat"/> may know of tick <paramref name="sim"/>.Tick (design §5.5).</summary>
    private void Build(BattleSnapshot s, BattleSeat seat, Simulation sim, VisionTracker vision, MatchRules rules)
    {
        Viewer viewer = ViewerOf(seat, sim);
        s.Tick = sim.Tick;
        int n = 0;
        s.HasOwn = false;
        foreach (ref readonly TankState t in sim.Tanks)
        {
            if (!viewer.Sees(t, vision)) continue;
            BattleSeat? owner = SeatOfTank(t.Id);
            s.Entities[n++] = ToEntity(t, owner?.Slot ?? MatchRules.NoSlot, owner?.TankIndex ?? 0, sim.Tick);
            if (t.Id == viewer.OwnTankId)
            {
                s.HasOwn = true;
                s.Own = ToOwn(t);
            }
        }
        s.EntityCount = n;
        s.Entities.AsSpan(0, n).Sort(ByIdComparison);   // a span sort with a cached delegate: Array.Sort with a comparer allocates one per call
        ref readonly Standing mine = ref rules.Of(seat.Slot);
        s.Match = new MatchBlock
        {
            Phase = rules.Phase,
            TicksLeft = rules.TicksLeft,
            Spotted = viewer.IsSpotted(sim, vision),
            WinnerTeam = rules.WinnerTeam,
            WinnerSlot = rules.WinnerSlot,
            RespawnIn = mine.RespawnAt >= 0 ? System.Math.Max(0, mine.RespawnAt - sim.Tick) : 0,
            TeamScore0 = rules.TeamScore(0),
            TeamScore1 = rules.TeamScore(1),
            // the bases' progress goes to everyone, seen or not (stage 5 design §3.3): standing in a base doesn't spot a tank
            BaseProgress0 = rules.Capture?.Progress(0) ?? 0,
            BaseProgress1 = rules.Capture?.Progress(1) ?? 0,
            EndReason = rules.EndReason,
        };
        bool playing = rules.Phase == MatchPhase.Playing;
        for (byte slot = 0; slot < s.Players.Length; slot++)
        {
            ref readonly Standing p = ref rules.Of(slot);
            s.Players[slot] = p.Active ? new PlayerStats
            {
                Active = true, Team = p.Team, Spec = _seats[slot]?.TankIndex ?? 0, TankId = p.TankId,
                Kills = p.Kills, Deaths = p.Deaths, Alive = p.Alive, Rank = p.Rank, Capture = CaptureFor(viewer, p, slot, playing, rules),
            } : default;   // an empty line, not the leaver's stale one (the wire drops inactive fields anyway)
        }
    }

    /// <summary>
    /// The capture the viewer may read on <paramref name="slot"/>'s line: its own and its team-mates' always, an enemy's
    /// only once the match has ended (0 till then). Standing in a base doesn't spot a tank (stage 5 design §3.3), and an
    /// enemy's capture ticking up would tell the defenders who is in it. The viewer's team is the seat's, as for the
    /// spotted lamp (<see cref="ViewerOf"/>), alive, a wreck or watching.
    /// </summary>
    private static byte CaptureFor(Viewer viewer, in Standing line, byte slot, bool playing, MatchRules rules) =>
        playing && line.Team != viewer.Team ? (byte)0 : rules.CaptureBest(slot);

    private static readonly Comparison<BattleEntity> ByIdComparison = static (a, b) => a.Id.CompareTo(b.Id);

    /// <param name="tick">The snapshot's tick: protection is a window, so the flag needs the time (<see cref="Simulation.IsProtected"/>).</param>
    public static BattleEntity ToEntity(in TankState t, byte slot, byte spec, int tick) => new()
    {
        Id = t.Id,
        Slot = slot,
        Spec = spec,
        Team = t.Team,
        X = BattleProtocol.ToCm(t.Position.X),
        Z = BattleProtocol.ToCm(t.Position.Y),
        Yaw = (ushort)BattleProtocol.QuantizeAngle(t.Yaw, BattleProtocol.YawBits),
        TurretYaw = (ushort)BattleProtocol.QuantizeAngle(t.TurretYaw, BattleProtocol.YawBits),
        Pitch = (ushort)BattleProtocol.QuantizePitch(t.GunPitch, BattleProtocol.PitchBits),
        Speed = (short)MathF.Round(t.Speed / BattleProtocol.SpeedUnit),
        Health = (ushort)System.Math.Clamp(t.Health, 0, ushort.MaxValue),
        Flags = (t.IsDestroyed ? TankFlags.Destroyed : TankFlags.None) | (tick < t.ProtectedUntil ? TankFlags.Protected : TankFlags.None),
    };

    public static OwnTank ToOwn(in TankState t) => new()
    {
        Id = t.Id, X = t.Position.X, Z = t.Position.Y, Yaw = t.Yaw, TurretYaw = t.TurretYaw, GunPitch = t.GunPitch, Speed = t.Speed,
        Reload = t.Reload, Bloom = t.Bloom, Ammo = t.Ammo, ApLeft = t.ApLeft, HeLeft = t.HeLeft, ShotCount = t.ShotCount, Health = t.Health,
        ProtectedUntil = t.ProtectedUntil, LastFired = t.LastFired, Destroyed = t.IsDestroyed,
    };

    /// <summary>
    /// The match as it stands (with the bases in a standard battle), for a seat that joins, rejoins or resyncs: with the
    /// map's prepared bytes copied in (not encoded again), or without the map for a connection that has it
    /// (<paramref name="withMap"/> false).
    /// </summary>
    private void SendMatchStart(BattleSeat seat, bool withMap)
    {
        Simulation sim = _sim!;
        PreparedMap map = _matchMap!;
        var props = new List<PropState>();
        foreach (Prop p in sim.Field.Props)
        {
            if (!p.IsDestructible) continue;
            int left = sim.Field.Durability(p.Id);
            if (left != p.MaxDurability) props.Add(new PropState(p.Id, BattleProtocol.QuantizeDurability(left, p.MaxDurability)));
        }
        // the bases only where they count: a client draws the circles of a standard battle alone
        IReadOnlyList<TeamBase>? bases = ModeRules.HasBases(_mode) ? map.Definition.Bases : null;
        var start = new MatchStartMessage(_mode, _matchNumber, sim.Tick, _rules!.EndTick, withMap ? map.Data : null, props, TankOf(seat.Slot), bases);
        _mapWriter.Reset();
        if (withMap) BattleMessages.Write(_mapWriter, start, map.EncodedMap);
        else BattleMessages.Write(_mapWriter, start);
        Send(seat, _mapWriter.Data, Channel.Reliable);
        if (withMap && seat.Connected) seat.MapSentForMatch = _matchNumber;
    }
}
