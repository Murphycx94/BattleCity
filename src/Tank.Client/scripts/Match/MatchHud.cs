using Godot;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The match HUD's root (blueprint §1.1): a canvas layer above the combat HUD that owns the score plate, the capture
/// bars under it (standard battle) or the zone line (royale), the outside-the-zone warning under everything, the kill feed, the centre banner and the F3 panel, and hands each the part of a <see cref="MatchHudFrame"/> it draws. The scene calls
/// <see cref="Refresh"/> once a frame and <see cref="OnKill"/> (or <see cref="OnZoneKill"/>) for every kill event; the gallery does the same with canned
/// data. While the scoreboard is up (Tab held, <see cref="SetScoreboardOpen"/>, or the results screen once the match has
/// ended) the kill feed and the F3 panel step aside: the scoreboard is wider than the room between them; so do the capture
/// bars and the zone line, which sit where the board's top does, and the warning's words, whose box lies in the board's
/// rows; the warning's tint stays under the board and goes only with the results. A royale adds the crates' two blocks
/// (stage 6 design §4.3): the pickup toasts under the banner (<see cref="OnPickup"/>) and the buff row over the status
/// panel (<see cref="MatchHudFrame.Buffs"/>); both step aside under the board too, and draw nothing while empty.
/// </summary>
public partial class MatchHud : CanvasLayer
{
    private const int HudLayer = 2;

    private readonly KillFeed _feed = new();
    private readonly PickupFeed _pickups = new();
    private ZoneWarningView _warning = null!;
    private PickupToastView _toasts = null!;
    private BuffRowView _buffs = null!;
    private ScorePlate _plate = null!;
    private CaptureBarsView _capture = null!;
    private ZoneLineView _zoneLine = null!;
    private KillFeedView _feedView = null!;
    private CenterBanner _banner = null!;
    private ScoreboardView _board = null!;
    private NetPanel _net = null!;
    private bool _scoreboardOpen;
    private bool _boardShown;   // the board is up: Tab is held, or the results screen is
    private bool _resultsUp;    // the match has ended: the results screen covers the plate
    private bool _netOpen;

    public MatchHud() => Layer = HudLayer;

    public override void _Ready()
    {
        _warning = Add(new ZoneWarningView());   // first: its tint lies under the plate and the feed
        _toasts = Add(new PickupToastView(_pickups));
        _buffs = Add(new BuffRowView());
        _plate = Add(new ScorePlate());
        _capture = Add(new CaptureBarsView());
        _zoneLine = Add(new ZoneLineView());
        _feedView = Add(new KillFeedView(_feed));
        _banner = Add(new CenterBanner());
        _board = Add(new ScoreboardView());   // after the banner: the results' dimming lies under the table
        _net = Add(new NetPanel { Visible = false });
    }

    /// <summary>Tab is held or let go: the scoreboard shows or hides (the results screen shows it regardless).</summary>
    public void SetScoreboardOpen(bool open) => _scoreboardOpen = open;

    /// <summary>A kill for the feed; <paramref name="killer"/> is null when the shooter is hidden from the player.</summary>
    public void OnKill(string? killer, Side killerSide, string victim, Side victimSide, bool friendly, double now) =>
        _feed.Add(killer, killerSide, victim, victimSide, friendly, now);

    /// <summary>A tank the zone finished, for the feed: "victim 死于毒圈".</summary>
    public void OnZoneKill(string victim, Side victimSide, double now) => _feed.AddZone(victim, victimSide, now);

    /// <summary>The player's own pickup (the server's <see cref="CrateMessage"/>), for a toast from <paramref name="now"/> on.</summary>
    public void OnPickup(in CrateMessage m, double now) => _pickups.Add(m.Kind, m.Amount, m.Amount2, m.Layers, now);

    /// <summary>A canned toast (the gallery): <paramref name="line"/>'s words, to go at its <see cref="PickupLine.ExpiresAt"/>.</summary>
    public void AddPickup(in PickupLine line) => _pickups.Add(line.Kind, line.Amount, line.Amount2, line.Layers, line.ExpiresAt - PickupFeed.Lifetime);

    /// <summary>One frame: <paramref name="now"/> is the scene's clock in seconds (the feed's lines expire by it).</summary>
    public void Refresh(in MatchHudFrame frame, bool netPanelOpen, double now)
    {
        _warning.Update(frame);
        _toasts.Update(now);
        _buffs.Update(frame.Buffs);
        _plate.Update(frame);
        _capture.Update(frame, now);
        _zoneLine.Update(frame);
        _feedView.Update(now);
        _banner.Update(frame);
        _board.Update(frame, _scoreboardOpen);
        bool resultsUp = frame.Phase == MatchPhase.Ended;
        bool boardShown = _scoreboardOpen || resultsUp;
        if (boardShown != _boardShown || resultsUp != _resultsUp)
        {
            (_boardShown, _resultsUp) = (boardShown, resultsUp);
            ApplyVisibility();
        }
        if (netPanelOpen != _netOpen)
        {
            _netOpen = netPanelOpen;
            if (!netPanelOpen) _net.Reset();
            ApplyVisibility();
        }
        if (netPanelOpen) _net.Update(frame.Net, now);
    }

    private void ApplyVisibility()
    {
        _warning.Visible = !_resultsUp;    // the results screen has its own dimming
        _warning.ShowText(!_boardShown);   // the words' box lies in the board's rows; the tint stays under the board
        _plate.Visible = !_resultsUp;   // its clock would run into the title
        _capture.Visible = !_boardShown;   // the board (Tab, or the results) covers the bars' place
        _zoneLine.Visible = !_boardShown;   // and the zone line's
        _feedView.Visible = !_boardShown;
        _toasts.Visible = !_boardShown;   // the toasts' box lies in the board's rows
        _buffs.Visible = !_boardShown;    // and the buff row goes with the status panel's play HUD
        _net.Visible = _netOpen && !_boardShown;
    }

    private T Add<T>(T node) where T : Node
    {
        AddChild(node);
        return node;
    }
}
