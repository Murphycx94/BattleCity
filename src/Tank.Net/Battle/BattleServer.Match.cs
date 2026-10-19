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
    /// Starts a match in the room's mode with everyone seated, plus the bots asked for (named, given random tanks, put on
    /// the thinner team), if there are at least two tanks and, in a team mode, both teams have one.
    /// </summary>
    public bool TryStartMatch(out string? reason)
    {
        if (_sim is not null)
        {
            reason = "a match is running";
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

        _map = MapData.Of(SandboxMap.Create());
        _sim = new Simulation(_map.Build());
        _vision = new VisionTracker(_sim);
        _rules = new MatchRules(_mode, _sim, SandboxMap.Spawns, _options.MatchSeconds is { } seconds ? System.Math.Max(1, (int)(seconds * SimConstants.TickRate)) : null);
        _matchNumber++;
        _endedAtFrame = -1;
        foreach (BattleSeat? seat in _seats)
        {
            if (seat is null) continue;
            PutIntoMatch(seat);
            if (seat.IsBot) seat.Bot = new SkirmishBot(_rules.Of(seat.Slot).TankId, SandboxMap.Waypoints, seed ^ (uint)(seat.Slot * BotSeedStride));
            else seat.ResetForMatch();
        }
        _log($"match started: {ModeRules.Id(_mode)}, {humans} players, {seated - humans} bots, seed {seed}");
        foreach (BattleSeat seat in _byPeer.Values) SendMatchStart(seat);
        _roomDirty = true;
        reason = null;
        return true;
    }

    private void PutIntoMatch(BattleSeat seat)
    {
        _rules!.Join(seat.Slot, TankCatalog.All[seat.TankIndex], seat.Team);
        seat.InMatch = true;
    }

    public void EndMatch()
    {
        if (_sim is null) return;
        _log("match over, back to the room");
        _sim = null;
        _vision = null;
        _rules = null;
        _map = null;
        _endedAtFrame = -1;
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
            if (_endedAtFrame < 0) _endedAtFrame = _frame;
            if (_frame - _endedAtFrame >= _options.ReturnToRoomSeconds * SimConstants.TickRate) EndMatch();
        }
        else if (!AnyHumanSeated())   // seated, connected or not: a dropped player has the grace period to come back
        {
            _log("nobody left in the match");
            EndMatch();
        }
    }

    private bool AnyHumanSeated()
    {
        foreach (BattleSeat? seat in _seats)
            if (seat is { IsBot: false }) return true;
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
        Channel channel = _writer.ByteLength > MaxUnreliableBytes ? Channel.Reliable : Channel.Unreliable;
        Send(seat, _writer.Data, channel);
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
        };
        for (byte slot = 0; slot < s.Players.Length; slot++)
        {
            ref readonly Standing p = ref rules.Of(slot);
            s.Players[slot] = p.Active ? new PlayerStats
            {
                Active = true, Team = p.Team, Spec = _seats[slot]?.TankIndex ?? 0, TankId = p.TankId,
                Kills = p.Kills, Deaths = p.Deaths, Alive = p.Alive, Rank = p.Rank,
            } : default;   // an empty line, not the leaver's stale one (the wire drops inactive fields anyway)
        }
    }

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

    private void SendMatchStart(BattleSeat seat)
    {
        Simulation sim = _sim!;
        var props = new List<PropState>();
        foreach (Prop p in sim.Field.Props)
        {
            if (!p.IsDestructible) continue;
            int left = sim.Field.Durability(p.Id);
            if (left != p.MaxDurability) props.Add(new PropState(p.Id, BattleProtocol.QuantizeDurability(left, p.MaxDurability)));
        }
        _mapWriter.Reset();
        BattleMessages.Write(_mapWriter, new MatchStartMessage(_mode, _matchNumber, sim.Tick, _rules!.EndTick, _map!, props, TankOf(seat.Slot)));
        Send(seat, _mapWriter.Data, Channel.Reliable);
    }
}
