using System.Net.NetworkInformation;
using System.Net.Sockets;
using Godot;
using Tank.Client.Controls;
using Tank.Client.UI;
using Tank.Client.View;
using Tank.Core.World;
using Tank.Net.Client;
using Tank.Net.Transport;

namespace Tank.Client.Bootstrap;

/// <summary>Root node: screen flow (menu → lobby → match), per-frame client update, bots and automation.</summary>
public partial class Main : Node
{
    private readonly Session _session = new();
    private readonly InputSampler _input = new();
    private readonly List<(GameClient Client, int Index)> _bots = new();
    private LaunchOptions _options = new();
    private MenuScreen _menu = null!;
    private LobbyScreen _lobby = null!;
    private GameView? _game;
    private Hud? _hud;
    private LanDiscovery? _discovery;
    private ServerStatsSource? _serverStats;
    private double _time;
    private bool _netGraph;
    private bool _autoStartSent;
    private bool _screenshotTaken;
    private double _nextStatsLog = 1;

    public override void _Ready()
    {
        _options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        _netGraph = _options.NetGraph;
        GetWindow().Title = "坦克 3D";
        _menu = new MenuScreen();
        _menu.HostRequested += (name, mode) => Host(name, mode);
        _menu.JoinRequested += (name, host, port) => Join(name, host, port);
        AddChild(_menu);
        _menu.PlayerName = _options.Name ?? System.Environment.UserName;

        _lobby = new LobbyScreen { Visible = false, LocalAddresses = string.Join(", ", LocalIPv4()) };
        _lobby.LeaveRequested += () => LeaveToMenu("已离开房间。");
        AddChild(_lobby);

        if (_options.Host) Host(_menu.PlayerName, _options.Mode);
        else if (_options.JoinAddress is { } address) JoinAddress(address);
        InitUpdates();
    }

    public override void _Input(InputEvent e) => _input.OnInputEvent(e);

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("toggle_netgraph")) _netGraph = !_netGraph;
        if (e.IsActionPressed("toggle_camera")) _game?.Rig.Toggle();
        if (e.IsActionPressed("ui_cancel") && _session.Client is not null) LeaveToMenu("已离开房间。");
    }

    public override void _Process(double delta)
    {
        _time += delta;
        var client = _session.Client;
        if (client is null)
        {
            PollDiscovery();
            PollUpdates();
        }
        else
        {
            var input = client.Phase == ClientPhase.InMatch ? _input.Sample() : LocalInput.None;
            client.Update(delta, input);
            foreach (var (bot, index) in _bots) bot.Update(delta, BotDriver.Think(index, _time));
            UpdateScreens(client, delta);
        }
        RunAutomation();
    }

    public override void _ExitTree()
    {
        foreach (var (bot, _) in _bots) bot.Dispose();
        _discovery?.Dispose();
        _update?.Dispose();
        _session.Dispose();
    }

    private void UpdateScreens(GameClient client, double delta)
    {
        switch (client.Phase)
        {
            case ClientPhase.Connecting:
            case ClientPhase.Lobby:
                ShowLobby(client);
                if (_options.AutoStart && !_autoStartSent && client.IsHost && client.Lobby?.Players.Count >= 1 + _bots.Count)
                {
                    _autoStartSent = true;
                    client.StartMatch();
                }
                break;
            case ClientPhase.InMatch:
                ShowGame();
                _game!.Sync(client, (float)delta, _time);
                _hud!.NetGraphVisible = _netGraph;
                _hud.Refresh(client, delta, Godot.Input.IsActionPressed("scoreboard"), _serverStats);
                if (_options.NetGraph && _time >= _nextStatsLog) LogStats(client);
                break;
            case ClientPhase.Disconnected:
                LeaveToMenu(client.DisconnectReason is { } r ? $"连接已断开：{DescribeDisconnect(r)}" : "连接已断开。", error: true);
                break;
        }
    }

    private void Host(string name, GameMode mode)
    {
        try
        {
            _session.Host(name, $"{name}的房间", _options.Port, mode);
            var runner = _session.Runner!;
            _serverStats = new ServerStatsSource(() => (runner.AverageTickMs, runner.MaxTickMs));
            StopDiscovery();
            for (int i = 0; i < _options.Bots; i++)
            {
                var bot = new GameClient(new LiteNetClientTransport("127.0.0.1", _options.Port), $"机器人 {i + 1}", () => _session.Now);
                _bots.Add((bot, i));
            }
            _menu.SetStatus("正在启动服务器…");
        }
        catch (InvalidOperationException ex)
        {
            _session.Leave();
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

    private void Join(string name, string host, int port)
    {
        StopDiscovery();
        _session.Join(host, port, name);
        _menu.SetStatus($"正在连接 {host}:{port}…");
    }

    private void LeaveToMenu(string message, bool error = false)
    {
        foreach (var (bot, _) in _bots) bot.Dispose();
        _bots.Clear();
        _session.Leave();
        _serverStats = null;
        _autoStartSent = false;
        HideGame();
        _lobby.Visible = false;
        _menu.Visible = true;
        _menu.SetStatus(message, error);
    }

    private void ShowLobby(GameClient client)
    {
        _menu.Visible = false;
        EnsureGameView();
        _hud!.Visible = false;
        if (client.Lobby is { } lobby) _game!.ShowPreview(lobby.MapName, (float)GetProcessDeltaTime(), _time);
        if (!_lobby.Visible)
        {
            _lobby.Bind(client);
            _lobby.Visible = true;
        }
        _lobby.Refresh();
    }

    private void ShowGame()
    {
        _menu.Visible = false;
        _lobby.Visible = false;
        EnsureGameView();
        _hud!.Visible = true;
    }

    private void EnsureGameView()
    {
        if (_game is not null) return;
        _game = new GameView();
        AddChild(_game);
        MoveChild(_game, 0); // draw the 3D world behind the UI screens
        if (_options.ChaseCamera) _game.Rig.Mode = CameraMode.Chase;
        _hud = new Hud();
        AddChild(_hud);
    }

    private void HideGame()
    {
        _game?.QueueFree();
        _game = null;
        _hud?.QueueFree();
        _hud = null;
    }

    private void PollDiscovery()
    {
        if (!_menu.Visible) return;
        try
        {
            _discovery ??= new LanDiscovery();
            _discovery.Poll();
            _menu.ShowRooms(_discovery.Rooms);
        }
        catch (Exception ex) when (ex is InvalidOperationException or SocketException)
        {
            _menu.SetStatus($"局域网搜索不可用（{ex.Message}），仍可通过地址加入。", error: true);
            _discovery?.Dispose();
            _discovery = null;
        }
    }

    private void StopDiscovery()
    {
        _discovery?.Dispose();
        _discovery = null;
    }

    private void RunAutomation()
    {
        if (_options.ScreenshotPath is { } path && !_screenshotTaken && _time >= _options.ScreenshotAt)
        {
            _screenshotTaken = true;
            var image = GetViewport().GetTexture().GetImage();
            var err = image.SavePng(path);
            GD.Print(err == Error.Ok ? $"screenshot saved: {path}" : $"screenshot failed: {err}");
        }
        if (_options.QuitAfter > 0 && _time >= _options.QuitAfter) GetTree().Quit();
    }

    private void LogStats(GameClient client)
    {
        _nextStatsLog = _time + 1;
        var s = client.Stats;
        GD.Print($"[net] t={_time:F1} fps={Engine.GetFramesPerSecond()} rtt={s.RttMs} jitter={s.JitterMs:F1} interp={s.InterpDelayMs:F0} " +
                 $"slack={s.InputSlackTicks:F2} rate={s.ClockRate:F3} snaps={s.SnapshotsPerSecond} corr={s.Mispredictions} " +
                 $"extrap={s.Extrapolations} down={s.BytesInPerSecond / 1024:F1}KB/s");
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
