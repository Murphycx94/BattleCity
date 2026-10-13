using Tank.Core;
using Tank.Core.World;
using Tank.Net.Protocol;

namespace Tank.Net.Server;

public sealed partial class GameServer
{
    private const int RejectDisconnectDelayFrames = 30;

    private void HandleHello(int peerId, HelloMessage hello)
    {
        if (_byPeer.ContainsKey(peerId) || _pendingKicks.Exists(k => k.PeerId == peerId)) return;
        _awaitingHello.Remove(peerId);
        if (hello.ProtocolVersion != ProtocolInfo.Version)
        {
            Reject(peerId, $"版本不一致：房主 v{ProtocolInfo.Version}，你的客户端 v{hello.ProtocolVersion}。");
            return;
        }

        bool isOwner = _options.OwnerToken is { } owner && owner != 0 && hello.SessionToken == owner;
        var conn = FindReconnect(hello.SessionToken) ?? AllocateSlot(isOwner ? hello.SessionToken : null);
        if (conn is null)
        {
            Reject(peerId, "房间已满。");
            return;
        }

        conn.PeerId = peerId;
        conn.DisconnectedAtTick = -1;
        conn.Name = SanitizeName(hello.Name, conn.Slot);
        _byPeer[peerId] = conn;
        if (_hostSlot == Teams.NoSlot || isOwner) _hostSlot = conn.Slot;
        _log($"slot {conn.Slot} joined as '{conn.Name}'");

        _writer.Reset();
        Messages.Write(_writer, new WelcomeMessage(conn.Slot, conn.Token));
        _transport.Send(peerId, _writer.Data, Channel.Reliable);
        _lobbyDirty = true;

        if (_sim is null) return;
        if (!_sim.State.Players[conn.Slot].Active) _sim.AddPlayer(conn.Slot, TeamFor(conn));
        conn.Inputs.Reset();
        conn.AckedSnapshotTick = 0;
        SendMatchStart(conn, _sim);
    }

    private ClientConnection? FindReconnect(uint token)
    {
        if (token == 0) return null;
        return _slots.FirstOrDefault(c => c is { Connected: false } && c.Token == token);
    }

    private ClientConnection? AllocateSlot(uint? token = null)
    {
        for (byte slot = 0; slot < Math.Min(_slots.Length, _options.MaxPlayers); slot++)
        {
            if (_slots[slot] is not null) continue;
            var conn = new ClientConnection(slot, token ?? NewToken());
            conn.Team = DefaultTeam(slot);
            _slots[slot] = conn;
            return conn;
        }
        return null;
    }

    /// <summary>Reconnect tokens let a client reclaim its slot, so they must not be guessable.</summary>
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
        Messages.Write(_writer, new RejectMessage(reason));
        _transport.Send(peerId, _writer.Data, Channel.Reliable);
        _pendingKicks.Add((peerId, _frame + RejectDisconnectDelayFrames));
    }

    private void HandleLobbyCommand(ClientConnection conn, LobbyCommandMessage cmd)
    {
        bool isHost = conn.Slot == _hostSlot;
        switch (cmd.Kind)
        {
            case LobbyCommandKind.SetTeam when _sim is null && _mode == GameMode.BaseBattle:
                if (byte.TryParse(cmd.Arg, out byte team) && team <= 1) conn.Team = team;
                break;
            case LobbyCommandKind.SetReady:
                conn.Ready = cmd.Arg == "1";
                break;
            case LobbyCommandKind.SetMode when isHost && _sim is null:
                if (Enum.TryParse(cmd.Arg, ignoreCase: true, out GameMode mode) && Enum.IsDefined(mode))
                {
                    _mode = mode;
                    _mapName = MapLibrary.DefaultFor(mode);
                    foreach (var c in _slots) if (c is not null) c.Team = DefaultTeam(c.Slot);
                }
                break;
            case LobbyCommandKind.SetMap when isHost && _sim is null:
                if (MapLibrary.Names.Contains(cmd.Arg)) _mapName = cmd.Arg;
                break;
            case LobbyCommandKind.StartMatch when isHost && _sim is null:
                StartMatch();
                break;
            case LobbyCommandKind.ReturnToLobby when isHost && _sim is not null:
                EndMatch();
                break;
            default:
                return;
        }
        _lobbyDirty = true;
    }

    public void StartMatch()
    {
        uint seed = _options.Seed ?? (uint)Random.Shared.Next(1, int.MaxValue);
        var config = new MatchConfig(_mode, MapLibrary.Load(_mapName), seed, _options.Stage, _options.EnemyTotal);
        var sim = new Simulation(config);
        foreach (var conn in _slots)
        {
            if (conn is null) continue;
            sim.AddPlayer(conn.Slot, TeamFor(conn));
            conn.Inputs.Reset();
            conn.AckedSnapshotTick = 0;
        }
        foreach (var snap in _history) snap.Tick = -1;
        _sim = sim;
        _matchNumber++;
        _endedAtFrame = -1;
        _log($"match started: {_mode} on {_mapName}, seed {seed}");
        foreach (var conn in _byPeer.Values) SendMatchStart(conn, sim);
        _lobbyDirty = true;
    }

    public void EndMatch()
    {
        if (_sim is null) return;
        _log("match over, back to lobby");
        _sim = null;
        _endedAtFrame = -1;
        for (byte slot = 0; slot < _slots.Length; slot++)
        {
            var conn = _slots[slot];
            if (conn is null) continue;
            if (!conn.Connected) _slots[slot] = null;
            else conn.Ready = false;
        }
        _lobbyDirty = true;
    }

    private void SendMatchStart(ClientConnection conn, Simulation sim)
    {
        var s = sim.State;
        var msg = new MatchStartMessage(s.Config.Mode, _mapName, MapLibrary.GetSource(_mapName), s.Config.Seed,
            s.Config.Stage, s.Config.EnemyTotal, s.Tick, s.Map.Cells.ToArray(), _matchNumber);
        _writer.Reset();
        Messages.Write(_writer, msg);
        _transport.Send(conn.PeerId, _writer.Data, Channel.Reliable);
    }

    private void BroadcastLobby()
    {
        _lobbyDirty = false;
        var players = _slots.Where(c => c is not null)
            .Select(c => new LobbyPlayer(c!.Slot, c.Name, c.Team, c.Ready, c.Connected))
            .ToList();
        _writer.Reset();
        Messages.Write(_writer, new LobbyStateMessage(_mode, _mapName, _hostSlot, MatchRunning, players));
        SendToAll(_writer.Data, Channel.Reliable);
    }

    private void ElectHost()
    {
        _hostSlot = Teams.NoSlot;
        foreach (var c in _slots)
        {
            if (c is not { Connected: true }) continue;
            _hostSlot = c.Slot;
            return;
        }
    }

    private byte DefaultTeam(byte slot) => _mode switch
    {
        GameMode.BaseBattle => (byte)(_slots.Count(c => c is not null && c.Slot != slot && c.Team == 0) >
                                      _slots.Count(c => c is not null && c.Slot != slot && c.Team == 1) ? 1 : 0),
        GameMode.Deathmatch => slot,
        _ => 0,
    };

    private byte TeamFor(ClientConnection conn) => _mode switch
    {
        GameMode.Deathmatch => conn.Slot,
        GameMode.Coop => 0,
        _ => conn.Team,
    };

    private static string SanitizeName(string name, byte slot)
    {
        var clean = new string((name ?? string.Empty).Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (clean.Length > ProtocolInfo.MaxPlayerNameBytes) clean = clean[..ProtocolInfo.MaxPlayerNameBytes];
        return clean.Length == 0 ? $"Player {slot + 1}" : clean;
    }
}
