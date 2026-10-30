using System.Net.NetworkInformation;
using System.Net.Sockets;
using Godot;
using Tank.Client.Match;
using Tank.Client.Online;
using Tank.Client.Sandbox;
using Tank.Client.UI;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Client.Bootstrap;

/// <summary>Main's transitions: hosting and joining, leaving, entering and leaving the match, and the sandbox.</summary>
public partial class Main
{
    private readonly SeatTokens _seatTokens = new();
    private (string Host, int Port)? _joined;   // the room the session joined, for its token; null when hosting

    private void Host(string name)
    {
        LeaveOld();
        _joined = null;
        // A room opens on the mode's own map when it has one (a royale: the riverland), else on --map when given, else on the riverland (stage 5 design §2).
        var settings = new HostSettings(name, $"{name}的房间", _options.Port, _options.Mode ?? BattleMode.TeamBattle, _options.Bots, _options.Seed,
            _options.MatchSeconds, _options.HostMap);
        try
        {
            BattleSession session = _options.Loopback
                ? BattleSession.HostLoopback(settings, ServerLog)
                : BattleSession.Host(settings, ServerLog);
            StartSession(session, "正在启动房间…");
        }
        catch (InvalidOperationException ex)   // the port is taken (a second instance on this machine), or no socket
        {
            _menu.SetStatus(ex.Message, error: true);
        }
    }

    private void JoinAddress(string address)
    {
        int port = _options.Port;
        int colon = address.LastIndexOf(':');
        if (colon > 0 && int.TryParse(address[(colon + 1)..], out int p))
        {
            port = p;
            address = address[..colon];
        }
        Join(_menu.PlayerName, address, port);
    }

    /// <summary>
    /// Joins the room at <paramref name="host"/>:<paramref name="port"/> with the token it gave this game last time, if
    /// any (<see cref="SeatTokens"/>): in any mode a player back within the grace period gets the seat still held, and
    /// one who left a match without respawns can't come back into it with a fresh tank.
    /// </summary>
    private void Join(string name, string host, int port)
    {
        LeaveOld();
        _joined = (host, port);
        try
        {
            StartSession(BattleSession.Join(host, port, name, _seatTokens.For(host, port)), $"正在连接 {host}:{port}…");
        }
        catch (Exception ex) when (ex is InvalidOperationException or SocketException or ArgumentException)
        {
            _menu.SetStatus($"无法连接：{ex.Message}", error: true);
        }
    }

    /// <summary>
    /// Before a new session is built, the old one goes, and the LAN search's socket with it: the menu is still up while a
    /// session is on its way in, and a hosting one holds the port a new host would bind ("port busy").
    /// </summary>
    private void LeaveOld()
    {
        LeaveSession(null);
        StopLan();
    }

    private void StartSession(BattleSession session, string status)
    {
        _session = session;
        _reconnect = new Reconnector();
        // --bots is only a request when given; 0 is also the default, and the host's own settings already carry it.
        _auto = new AutoRoom(_options.Tank, _options.Mode, _options.Bots > 0 ? _options.Bots : null, _options.AutoStart);
        _room.Bind(session.Client, LocalAddresses(session));
        _menu.SetStatus(status);
    }

    /// <summary>Drops the session (the client, then the server thread and socket) and shows the menu with <paramref name="message"/>.</summary>
    private void LeaveSession(string? message, bool error = false)
    {
        if (_matchView is not null) LeaveMatch();
        if (_session is { IsHosting: false } joined && _joined is { } room) _seatTokens.Remember(room.Host, room.Port, joined.Client.SessionToken);
        _session?.Dispose();
        _session = null;
        _reconnect = null;
        _auto = null;
        _room.Visible = false;
        _menu.Visible = true;
        if (message is not null) _menu.SetStatus(message, error);
    }

    private void ShowRoom(BattleClient client)
    {
        if (!_room.Visible)
        {
            _menu.Visible = false;
            _room.Visible = true;
        }
        _auto?.Next(client.Room, client.Slot, client.IsHost, _options.Players).SendTo(client);
        if (_auto?.TakeWarning() is { } warning) GD.PushWarning(warning);
        _room.Refresh();
    }

    /// <summary>The match began: the match scene takes the session (and the client's Update) until it reports <see cref="MatchMain.Finished"/>.</summary>
    private void EnterMatch(BattleClient client)
    {
        _menu.Visible = false;
        _room.Visible = false;
        GD.Print($"[match] entered match {client.Room?.Mode} on {client.Room?.Map} as slot {client.Slot}");
        var match = new MatchMain();
        match.Setup(_session!, _options, _screenshotTaken);
        _matchEntered = true;   // from now on the matches own the screenshot (RunAutomation)
        match.Finished += LeaveMatch;
        match.LeaveRequested += () => LeaveSession("已离开房间。");   // 离开房间 on the pause layer: the whole session goes
        _matchView = match;
        AddChild(match);
        MoveChild(match, 0);   // the 3D world behind the UI screens
    }

    /// <summary>The match is over (or the session ended): its scene goes, and the launch options may start the next one.</summary>
    private void LeaveMatch()
    {
        if (_matchView is { ScreenshotTaken: true }) _screenshotTaken = true;   // the next match must not take it again
        _matchView?.QueueFree();
        _matchView = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        WindowTitle.Set(this, MapText.AppTitle);
        _auto?.Reset();
    }

    /// <summary>
    /// The connection died (blueprint §5 T10). An unexpected drop of a seated client is retried with the same token, up
    /// to three times two seconds apart: the match scene has already left (its phase check fired), the menu shows
    /// 正在重连…, and the server's grace window hands the same slot back — mid-match it puts the client straight back in.
    /// The host's own client too: its server thread has kept running (a stalled host process timed out only its own link),
    /// and the owner token takes the host's seat and rights back. A deliberate close and a rejection go out at once, as
    /// before, with the described reason.
    /// </summary>
    private void OnDisconnected(BattleSession session, BattleClient client)
    {
        string reason = client.DisconnectReason is { } r ? DescribeDisconnect(r) : "连接已断开。";
        switch (_reconnect?.Observe(client.DisconnectReason, hadSeat: client.SessionToken != 0, _time) ?? Reconnector.Step.GiveUp)
        {
            case Reconnector.Step.Wait:
                if (_matchView is not null) LeaveMatch();
                _room.Visible = false;
                _menu.Visible = true;
                _menu.SetStatus($"连接断开，正在重连…（第 {_reconnect!.Attempt}/{Reconnector.MaxAttempts} 次）");
                break;
            case Reconnector.Step.Connect:
                try
                {
                    session.Reconnect();
                    _room.Bind(session.Client, LocalAddresses(session));
                }
                catch (Exception ex) when (ex is InvalidOperationException or SocketException or ArgumentException)   // as Join's
                {
                    GD.Print($"[reconnect] attempt {_reconnect!.Attempt} could not start a connection: {ex.Message}");
                    // the client stays disconnected: the next frame schedules another attempt, or gives up
                }
                break;
            case Reconnector.Step.GiveUp:
                LeaveSession(_reconnect is { Attempt: > 0 } ? $"重连失败：{reason}" : $"连接已断开：{reason}", error: true);
                break;
        }
    }

    /// <summary><c>--hud-demo NAME</c>: just the HUD gallery's screen; an unknown name lists the known ones and exits with code 2.</summary>
    private void EnterGallery(string name)
    {
        if (!HudDemoFrames.TryGet(name, out HudDemo demo))
        {
            GD.PrintErr($"Unknown --hud-demo '{name}'; use {string.Join(", ", HudDemoFrames.Names)}.");
            GetTree().Quit(2);
            return;
        }
        _menu.Visible = false;
        AddChild(new HudGallery(demo));
    }

    private void EnterSandbox()
    {
        if (_sandbox is not null) return;
        StopLan();
        LeaveSession(null);
        _menu.Visible = false;
        _sandbox = new SandboxMain();
        _sandbox.Setup(_options);
        _sandbox.ExitRequested += ExitSandbox;
        AddChild(_sandbox);
    }

    private void ExitSandbox()
    {
        _sandbox?.QueueFree();
        _sandbox = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        WindowTitle.Set(this, MapText.AppTitle);
        _menu.Visible = true;
        _menu.SetStatus("");
    }

    private void StopLan()
    {
        _lan?.Dispose();
        _lan = null;
    }

    private static void ServerLog(string message) => GD.Print($"[server] {message}");

    private string LocalAddresses(BattleSession session)
    {
        if (!session.IsHosting) return "";
        string suffix = _options.Port == BattleProtocol.DefaultPort ? "" : $":{_options.Port}";
        return string.Join(", ", LocalIPv4().Select(a => a + suffix));
    }

    /// <summary>Turns a transport disconnect code into player-facing text; server reject reasons pass through as-is.</summary>
    private static string DescribeDisconnect(string reason) => reason switch
    {
        "ConnectionFailed" => "无法连接到房主。",
        "Timeout" => "连接超时。",
        "HostUnreachable" or "NetworkUnreachable" or "UnknownHost" => "找不到房主，请检查地址和网络。",
        "RemoteConnectionClose" => "房主关闭了房间。",
        "DisconnectPeerCalled" => "已断开。",
        "ConnectionRejected" => "房主拒绝了连接。",
        "InvalidProtocol" => "协议不兼容。",
        _ => reason,
    };

    private static IEnumerable<string> LocalIPv4()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return Array.Empty<string>();
        }
    }
}
