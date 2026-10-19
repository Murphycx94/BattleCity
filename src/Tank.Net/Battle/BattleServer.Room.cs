using Tank.Net.Protocol;
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
        seat ??= AllocateSeat(isOwner ? hello.SessionToken : null);
        if (seat is null)
        {
            Reject(peerId, "房间已满。");
            return;
        }
        bool reconnecting = seat.DisconnectedAtFrame >= 0;
        seat.PeerId = peerId;
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
        if (!seat.InMatch && _rules.AcceptsJoins) PutIntoMatch(seat);
        if (seat.InMatch)
        {
            seat.ResetForMatch();
            SendMatchStart(seat);
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

    private BattleSeat? AllocateSeat(uint? token = null)
    {
        for (byte slot = 0; slot < _seats.Length; slot++)
        {
            if (_seats[slot] is not null) continue;
            var seat = new BattleSeat(slot, token ?? NewToken(), isBot: false) { Team = ThinnerTeam(slot) };
            _seats[slot] = seat;
            return seat;
        }
        return null;
    }

    /// <summary>Reconnect tokens let a client reclaim its seat, so they must not be guessable.</summary>
    private static uint NewToken()
    {
        uint token;
        do token = (uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue) ^ ((uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 2) << 31);
        while (token == 0);
        return token;
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
            case RoomCommandKind.SetTeam when !seat.InMatch && ModeRules.HasTeams(_mode):   // in the room, or waiting out a match
                if (byte.TryParse(cmd.Arg, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out byte team) && team <= 1) seat.Team = team;
                break;
            case RoomCommandKind.SetTank when !seat.InMatch:
                int index = IndexOfTank(cmd.Arg);
                if (index >= 0) seat.TankIndex = (byte)index;
                break;
            case RoomCommandKind.SetReady:
                seat.Ready = cmd.Arg == "1";
                break;
            case RoomCommandKind.SetMode when isHost && _sim is null:
                if (ModeRules.TryParse(cmd.Arg, out BattleMode mode)) _mode = mode;
                break;
            case RoomCommandKind.SetBots when isHost && _sim is null:
                if (int.TryParse(cmd.Arg, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int bots)) _bots = System.Math.Clamp(bots, 0, BattleProtocol.MaxPlayers - 1);
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
        return (byte)(b < a ? 1 : 0);
    }

    private void BroadcastRoom()
    {
        _roomDirty = false;
        var players = new List<RoomPlayer>();
        foreach (BattleSeat? s in _seats)
            if (s is not null) players.Add(new RoomPlayer(s.Slot, s.Name, ModeRules.HasTeams(_mode) ? s.Team : s.Slot, s.TankIndex, s.Ready, s.Connected || s.IsBot, s.IsBot));
        _writer.Reset();
        BattleMessages.Write(_writer, new RoomStateMessage(_mode, _hostSlot, (byte)_bots, MatchRunning, players));
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
