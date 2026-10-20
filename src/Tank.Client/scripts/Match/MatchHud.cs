using Godot;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The match HUD's root (blueprint §1.1): a canvas layer above the combat HUD that owns the score plate, the kill feed,
/// the centre banner and the F3 panel, and hands each the part of a <see cref="MatchHudFrame"/> it draws. The scene calls
/// <see cref="Refresh"/> once a frame and <see cref="OnKill"/> for every kill event; the gallery does the same with canned
/// data. While the scoreboard is up (Tab held, <see cref="SetScoreboardOpen"/>, or the results screen once the match has
/// ended) the kill feed and the F3 panel step aside: the scoreboard is wider than the room between them.
/// </summary>
public partial class MatchHud : CanvasLayer
{
    private const int HudLayer = 2;

    private readonly KillFeed _feed = new();
    private ScorePlate _plate = null!;
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
        _plate = Add(new ScorePlate());
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

    /// <summary>One frame: <paramref name="now"/> is the scene's clock in seconds (the feed's lines expire by it).</summary>
    public void Refresh(in MatchHudFrame frame, bool netPanelOpen, double now)
    {
        _plate.Update(frame);
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
        _plate.Visible = !_resultsUp;   // its clock would run into the title
        _feedView.Visible = !_boardShown;
        _net.Visible = _netOpen && !_boardShown;
    }

    private T Add<T>(T node) where T : Node
    {
        AddChild(node);
        return node;
    }
}
