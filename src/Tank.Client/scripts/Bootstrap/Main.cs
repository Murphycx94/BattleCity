using Godot;
using Tank.Client.Match;
using Tank.Client.Online;
using Tank.Client.Sandbox;
using Tank.Client.UI;
using Tank.Net.Battle;

namespace Tank.Client.Bootstrap;

/// <summary>
/// Root node and screen flow: menu → connecting → room → match → room, plus the sandbox as a child. One
/// <see cref="BattleSession"/> lives from "host" or "join" until leaving or a disconnect; the launch options drive the room
/// through <see cref="AutoRoom"/>. The flow itself (start, leave, enter and leave the match) is in Main.Flow.cs, the
/// update banner in Main.Update.cs.
/// </summary>
public partial class Main : Node
{
    private const double VirtualFrame = 1.0 / 60;   // a loopback host steps by this, not by the frame time

    private LaunchOptions _options = new();
    private MenuScreen _menu = null!;
    private RoomScreen _room = null!;
    private LanBrowser? _lan;
    private BattleSession? _session;
    private AutoRoom? _auto;
    private Reconnector? _reconnect;   // one per session: what to do when its connection dies
    private SandboxMain? _sandbox;
    private MatchMain? _matchView;
    private double _time;
    private bool _screenshotTaken;   // the run's one screenshot: taken by Main, or by a match (read back as its scene goes)
    private bool _matchEntered;      // a match scene has been entered: Main no longer takes the screenshot

    public override void _Ready()
    {
        _options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        foreach (string warning in _options.Warnings) GD.PushWarning(warning);
        WindowTitle.Set(this, MapText.AppTitle);
        _menu = new MenuScreen();
        _menu.HostRequested += Host;
        _menu.JoinRequested += Join;
        _menu.SandboxRequested += EnterSandbox;
        AddChild(_menu);
        _menu.PlayerName = _options.Name ?? System.Environment.UserName;

        _room = new RoomScreen { Visible = false };
        _room.LeaveRequested += () => LeaveSession("已离开房间。");
        AddChild(_room);

        if (_options.HudDemo is { } demo)
        {
            EnterGallery(demo);
            return;   // a gallery run has no session, no update check
        }
        if (_options.Sandbox) EnterSandbox();
        else if (_options.Host) Host(_menu.PlayerName);
        else if (_options.JoinAddress is { } address) JoinAddress(address);
        InitUpdates();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // In the room Esc leaves it; the match's own Esc handling comes with the match scene.
        if (e.IsActionPressed("ui_cancel") && _session is not null && _matchView is null) LeaveSession("已离开房间。");
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_sandbox is null)
        {
            if (_session is null)
            {
                PollLan();
                PollUpdates();
            }
            else RunSession(delta);
        }
        RunAutomation();
    }

    public override void _ExitTree()
    {
        _lan?.Dispose();
        _update?.Dispose();
        _session?.Dispose();
    }

    /// <summary>One frame with a session: the host's world, the room's launch options, the client, then whichever screen the phase calls for.</summary>
    private void RunSession(double delta)
    {
        BattleSession session = _session!;
        BattleClient client = session.Client;
        if (_matchView is null)
        {
            // The match scene owns Advance and Update while it exists: the client must see exactly one Update per frame (R1).
            double dt = session.IsVirtualClock ? VirtualFrame : delta;
            session.Advance(dt);
            client.Update(dt, LocalCommand.None);
        }
        switch (client.Phase)
        {
            case BattlePhase.Connecting:
                break;
            case BattlePhase.Room:
                _reconnect?.OnConnected();
                if (_matchView is not null) LeaveMatch();
                ShowRoom(client);
                break;
            case BattlePhase.InMatch:
                _reconnect?.OnConnected();
                if (_matchView is null) EnterMatch(client);   // the scene shows "loading" until the first snapshot brings the own tank
                break;
            case BattlePhase.Disconnected:
                OnDisconnected(session, client);
                break;
        }
    }

    private void PollLan()
    {
        if (!_menu.Visible) return;
        _lan ??= new LanBrowser();
        _lan.Poll();
        _menu.ShowRooms(_lan.Rows, _lan.Error);
    }

    private void RunAutomation()
    {
        // One screenshot per run (T9). Main takes a plain --screenshot-at on its own clock while no match has been entered
        // — the menu, the room, the HUD gallery; once a match scene is entered, the matches own it (counted from the scene's
        // creation, and --shot-after and --fx-shot wait for moments only a match or the sandbox has), and whoever takes
        // it first is the only one: Main hands its flag to each match and reads the match's back. A sandbox run schedules
        // its own. The timed quit stays here, on the process clock: --autostart starts match after match, and a quit
        // counted from each match's creation would never fall due were the match to keep ending sooner.
        if (_sandbox is null && !_matchEntered && !_screenshotTaken && _options.ScreenshotPath is { } path
            && ShotPlan.StartScheduleOnly(_options) && _time >= _options.ScreenshotAt)
        {
            _screenshotTaken = true;
            Image image = GetViewport().GetTexture().GetImage();
            Error err = image.SavePng(path);
            GD.Print(err == Error.Ok ? $"screenshot saved: {path}" : $"screenshot failed: {err}");
        }
        if (_options.QuitAfter > 0 && _time >= _options.QuitAfter) GetTree().Quit();
    }
}
