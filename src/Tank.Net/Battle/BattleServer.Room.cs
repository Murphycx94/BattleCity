using Tank.Net.Protocol;
using Tank.Sim.Ai;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Net.Battle;

public sealed partial class BattleServer
{
    private void HandleHello(int peerId, HelloMessage hello)
    {
        if (_byPeer.ContainsKey(peerId) || _pendingKicks.Exists(k => k.PeerId == peerId)) return;
        _awaitingHello.Remove(peerId);
        if (hello.ProtocolVersion != BattleProtocol.Version)
        {
            Reject(peerId, $"版本不一致：房主 v{BattleProtocol.Version}，你的客户端 v{hello.ProtocolVersion}。");
            return;
        }
        bool isOwner = _options.OwnerToken is { } owner && owner != 0 && hello.SessionToken == owner;
        BattleSeat? seat = SeatWithToken(hello.SessionToken);
        if (seat is { Connected: true }) TakeOver(seat);   // the same player from a new connection: the old one is dead to us
        // A player whose seat was freed during this match keeps the token in a new one, so the match still knows them.
        bool hadTank = seat is null && hello.SessionToken != 0 && _hadTank.Contains(hello.SessionToken);
        seat ??= AllocateSeat(isOwner || hadTank ? hello.SessionToken : null);
        if (seat is null)
        {
            Reject(peerId, "房间已满。");
            return;
        }
        bool reconnecting = seat.DisconnectedAtFrame >= 0;
        seat.PeerId = peerId;
        seat.MapSentForMatch = 0;   // a new connection: a new client, maybe, without the map
        seat.DisconnectedAtFrame = -1;
        seat.Name = SanitizeName(hello.Name, seat.Slot);
        _byPeer[peerId] = seat;
        if (_hostSlot == MatchRules.NoSlot || isOwner) _hostSlot = seat.Slot;
        _log($"slot {seat.Slot} {(reconnecting ? "reconnected" : "joined")} as '{seat.Name}'");

        _writer.Reset();
        BattleMessages.Write(_writer, new WelcomeMessage(seat.Slot, seat.Token));
        _transport.Send(peerId, _writer.Data, Channel.Reliable);
        _roomDirty = true;

        if (_sim is null || _rules is null) return;
        if (!seat.InMatch)
        {
            if (MayJoinMatch(seat)) PutIntoMatch(seat);
            else if (_rules.AcceptsJoins) _log($"slot {seat.Slot} already had a tank in this match; waiting for the next");
        }
        if (seat.InMatch)
        {
            seat.ResetForMatch();
            SendMatchStart(seat, withMap: true);
        }
    }

    /// <summary>The player's seat by reconnect token, connected or not; never a bot's, never for token 0.</summary>
    private BattleSeat? SeatWithToken(uint token)
    {
        if (token == 0) return null;
        foreach (BattleSeat? s in _seats)
            if (s is { IsBot: false } && s.Token == token) return s;
        return null;
    }

    /// <summary>
    /// Drops the connection a seat still holds (a client that switched networks reconnects before the old link times
    /// out). The old peer's disconnect, when it comes, finds no seat and is ignored.
    /// </summary>
    private void TakeOver(BattleSeat seat)
    {
        int old = seat.PeerId;
        _log($"slot {seat.Slot} ({seat.Name}) reconnecting over peer {old}; dropping it");
        _byPeer.Remove(old);
        _transport.Disconnect(old);
        seat.PeerId = -1;
        seat.DisconnectedAtFrame = _frame;
    }

    /// <summary>
    /// A new player's seat, or null with the room full: the server has <see cref="BattleProtocol.MaxPlayers"/> (16) seats, but a
    /// room takes only <see cref="ModeRules.MaxPlayersFor"/> its mode (10 outside a royale or duos, deviation 22). Every taken
    /// seat counts — players connected or within the reconnect grace, and during a match the bots — so a 10-seat match never
    /// holds an 11th tank; in the room, with no bots seated, that is the players alone. The seat's team: in duos the lowest
    /// empty one, else the lowest single (<see cref="DuoSeating.ForPerson"/>; the room has a seat for it, so never none), in
    /// every other mode the thinner of the two.
    /// </summary>
    private BattleSeat? AllocateSeat(uint? token = null)
    {
        if (_seats.Count(s => s is not null) >= ModeRules.MaxPlayersFor(_mode)) return null;
        for (byte slot = 0; slot < _seats.Length; slot++)
        {
            if (_seats[slot] is not null) continue;
            var seat = new BattleSeat(slot, token ?? NewToken(), isBot: false) { Team = ModeRules.IsDuos(_mode) ? DuoTeamForPerson() : ThinnerTeam(slot) };
            _seats[slot] = seat;
            return seat;
        }
        return null;
    }

    /// <summary>
    /// Reconnect tokens let a client reclaim its seat, so they must not be guessable — nor clash: never 0 (no token),
    /// never one a seat holds (whoever presented it would get that seat), one barred from this match (the newcomer would
    /// be turned away) or the owner's (it makes the host).
    /// </summary>
    private uint NewToken()
    {
        uint token;
        do token = (uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue) ^ ((uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 2) << 31);
        while (token == 0 || _hadTank.Contains(token) || token == _options.OwnerToken || SeatHolds(token));
        return token;
    }

    private bool SeatHolds(uint token)
    {
        foreach (BattleSeat? s in _seats)
            if (s is not null && s.Token == token) return true;
        return false;
    }

    private void Reject(int peerId, string reason)
    {
        _log($"rejecting peer {peerId}: {reason}");
        _writer.Reset();
        BattleMessages.Write(_writer, new RejectMessage(reason));
        _transport.Send(peerId, _writer.Data, Channel.Reliable);
        _pendingKicks.Add((peerId, _frame + RejectDisconnectDelayFrames));
    }

    private void HandleRoomCommand(BattleSeat seat, RoomCommandMessage cmd)
    {
        bool isHost = seat.Slot == _hostSlot;
        // A command whose argument doesn't parse ("-1", " 3", "x") or that its sender may not give is ignored: NumberStyles.None
        // takes digits only, and an unknown tank id or mode changes nothing.
        switch (cmd.Kind)
        {
            case RoomCommandKind.SetTeam when !seat.InMatch && ModeRules.TeamsFor(_mode) > 0:   // in the room, or waiting out a match; not where nobody picks
                if (byte.TryParse(cmd.Arg, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out byte team) && MayMoveTo(seat, team)) seat.Team = team;
                break;
            case RoomCommandKind.SetTank when !seat.InMatch:
                int index = IndexOfTank(cmd.Arg);
                if (index >= 0) seat.TankIndex = (byte)index;
                break;
            case RoomCommandKind.SetReady:
                seat.Ready = cmd.Arg == "1";
                break;
            case RoomCommandKind.SetMode when isHost && _sim is null:
                if (ModeRules.TryParse(cmd.Arg, out BattleMode mode)) SwitchMode(mode);
                break;
            case RoomCommandKind.SetBots when isHost && _sim is null:
                if (int.TryParse(cmd.Arg, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int bots)) _bots = System.Math.Clamp(bots, 0, ModeRules.MaxBotsFor(_mode));
                break;
            case RoomCommandKind.SetMap when isHost && _sim is null:
                if (FindMap(cmd.Arg) is { } map && ModeRules.AllowsMap(_mode, map.Id))   // a royale or duos stays on the riverland
                {
                    // Prepared first: nothing if it was before; in the foreground a generator that throws leaves the map
                    // Failed (logged, never thrown here), and the room says so. In the background it says Generating until then.
                    _maps.Prepare(map);
                    _roomMap = map;
                }
                break;
            case RoomCommandKind.StartMatch when isHost && _sim is null:
                if (!TryStartMatch(out string? why)) _log($"can't start: {why}");
                break;
            case RoomCommandKind.ReturnToRoom when isHost && _sim is not null:
                EndMatch();
                break;
            default:
                return;
        }
        _roomDirty = true;
    }

    /// <summary>
    /// The host's switch to <paramref name="mode"/>, if the room can make it: no more players than the mode seats (a royale or
    /// duos room of 11 or more stays one of the two), and for a mode with a map of its own (a royale, duos: the riverland) that map among
    /// <see cref="BattleServerOptions.Maps"/>, which the room then moves to (prepared first, as <c>SetMap</c> does). The
    /// bots are cut to what the mode seats. Into duos from another mode everyone is teamed afresh, out of it split over teams
    /// 0 and 1 again (<see cref="TeamPlayersForDuos"/>, <see cref="SplitPlayersOverTwoTeams"/>); any other switch moves nobody.
    /// </summary>
    private void SwitchMode(BattleMode mode)
    {
        string id = ModeRules.Id(mode);
        int humans = _seats.Count(s => s is { IsBot: false });
        if (humans > ModeRules.MaxPlayersFor(mode))
        {
            _log($"can't switch to {id}: {humans} players");
            return;
        }
        if (ModeRules.OnlyMap(mode) is { } only)
        {
            if (FindMap(only) is not { } map)
            {
                _log($"can't switch to {id}: no map {only}");
                return;
            }
            _maps.Prepare(map);
            _roomMap = map;
        }
        bool wasDuos = ModeRules.IsDuos(_mode);
        _mode = mode;
        _bots = System.Math.Min(_bots, ModeRules.MaxBotsFor(mode));
        if (ModeRules.IsDuos(mode) && !wasDuos) TeamPlayersForDuos();
        else if (wasDuos && !ModeRules.IsDuos(mode)) SplitPlayersOverTwoTeams();
    }

    /// <summary>
    /// The room turned duos: every player, in slot order, as if coming in afresh (<see cref="DuoSeating.ForPerson"/>), so
    /// the first eight get a team each and the rest partner them in turn. The room has no bots (they are seated only for a match).
    /// </summary>
    private void TeamPlayersForDuos()
    {
        foreach (BattleSeat? s in _seats)
            if (s is { IsBot: false }) s.Team = MatchRules.NoTeam;   // counted on no team until placed
        foreach (BattleSeat? s in _seats)
            if (s is { IsBot: false }) s.Team = DuoTeamForPerson();
    }

    /// <summary>
    /// The room left duos: every player, in slot order, onto the thinner of teams 0 and 1 of those placed before it (team 0
    /// on a tie), as <see cref="ThinnerTeam"/> places a newcomer. A free-for-all or a royale ignores the team, but outside
    /// duos no seat's team is above 1.
    /// </summary>
    private void SplitPlayersOverTwoTeams()
    {
        int onTeam0 = 0, onTeam1 = 0;
        foreach (BattleSeat? s in _seats)
        {
            if (s is not { IsBot: false }) continue;
            s.Team = BotSeating.ThinnerTeam(onTeam0, onTeam1);
            if (s.Team == 0) onTeam0++;
            else onTeam1++;
        }
    }

    /// <summary>
    /// Whether <paramref name="seat"/> may move to <paramref name="team"/>: in duos a team 0–7 with room for it
    /// (<see cref="DuoSeating.HasRoom"/>), in a two-team mode team 0 or 1.
    /// </summary>
    private bool MayMoveTo(BattleSeat seat, byte team)
    {
        if (!ModeRules.IsDuos(_mode)) return team < ModeRules.TeamsFor(_mode);
        Span<int> counts = stackalloc int[ModeRules.DuoTeams];
        CountDuoTeams(counts);
        return DuoSeating.HasRoom(counts, team, seat.Team);
    }

    /// <summary>The duos team for a player coming in now (<see cref="DuoSeating.ForPerson"/> of the seats taken).</summary>
    private byte DuoTeamForPerson()
    {
        Span<int> counts = stackalloc int[ModeRules.DuoTeams];
        CountDuoTeams(counts);
        return DuoSeating.ForPerson(counts);
    }

    /// <summary>
    /// The seats on each duos team now (<paramref name="counts"/>: <see cref="ModeRules.DuoTeams"/> long): every taken seat —
    /// players, those within the reconnect grace and, during a match or while one is being started, the bots — by its team. A
    /// seat on no team yet (<see cref="MatchRules.NoTeam"/>) counts nowhere.
    /// </summary>
    /// <remarks>
    /// Intended: during a duos match the counts include the match's bots, so a newcomer waiting for the next match is placed
    /// (<see cref="DuoSeating.ForPerson"/>) and may <c>SetTeam</c> only against them — the bots leave with the match and are seated
    /// afresh at the next start, partnering whoever is single then.
    /// </remarks>
    private void CountDuoTeams(Span<int> counts)
    {
        counts.Clear();
        foreach (BattleSeat? s in _seats)
            if (s is not null && s.Team < counts.Length) counts[s.Team]++;
    }

    /// <summary>The map of <see cref="BattleServerOptions.Maps"/> with the id <paramref name="id"/> (ignoring case), or null.</summary>
    private MapDefinition? FindMap(string id)
    {
        foreach (MapDefinition map in _options.Maps)
            if (string.Equals(map.Id, id, StringComparison.OrdinalIgnoreCase)) return map;
        return null;
    }

    private static int IndexOfTank(string id)
    {
        for (int i = 0; i < TankCatalog.All.Count; i++)
            if (TankCatalog.All[i].Id == id) return i;
        return -1;
    }

    /// <summary>The team with fewer seats (other than <paramref name="except"/>), team 0 on a tie.</summary>
    private byte ThinnerTeam(byte except)
    {
        int a = 0, b = 0;
        foreach (BattleSeat? s in _seats)
        {
            if (s is null || s.Slot == except) continue;
            if (s.Team == 0) a++;
            else b++;
        }
        return BotSeating.ThinnerTeam(a, b);
    }

    /// <summary>
    /// The room to everyone: each seat's team where seats are teamed (the two-team modes, duos: 0–7), its slot where they
    /// aren't (a free-for-all, a royale: each tank its own side).
    /// </summary>
    private void BroadcastRoom()
    {
        _roomDirty = false;
        var players = new List<RoomPlayer>();
        foreach (BattleSeat? s in _seats)
            if (s is not null) players.Add(new RoomPlayer(s.Slot, s.Name, ModeRules.TeamsFor(_mode) > 0 ? s.Team : s.Slot, s.TankIndex, s.Ready, s.Connected || s.IsBot, s.IsBot));
        _sentMapState = RoomMapState;
        _writer.Reset();
        BattleMessages.Write(_writer, new RoomStateMessage(_mode, _hostSlot, (byte)_bots, MatchRunning, players, _roomMap.Id, _sentMapState));
        SendToAll(_writer.Data, Channel.Reliable);
    }

    private void ElectHost()
    {
        _hostSlot = MatchRules.NoSlot;
        foreach (BattleSeat? s in _seats)
        {
            if (s is not { Connected: true }) continue;
            _hostSlot = s.Slot;
            return;
        }
    }

    private static string SanitizeName(string name, byte slot)
    {
        var clean = new string((name ?? string.Empty).Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (clean.Length > BattleProtocol.MaxNameBytes) clean = clean[..BattleProtocol.MaxNameBytes];
        return clean.Length == 0 ? $"Player {slot + 1}" : clean;
    }
}
