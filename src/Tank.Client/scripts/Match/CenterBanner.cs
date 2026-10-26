using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The banner in the middle of the screen (blueprint §4.2). Respawn: a plate at 0.28 of the height with "你被击毁了", the
/// seconds left in big type and "秒后复活" under it — and the spectate bar besides, when the waiting player watches a
/// teammate. Spectate: a bar at the bottom centre, above the status panel's row, with "观战 name · Q/E 切换" (or the
/// free camera's hints). Results: the screen dimmed, the title in big type with the line under it, and the
/// return countdown at the foot (the table between them is the scoreboard's). Otherwise nothing. Redraws only when the
/// kind, the second, the target, the free camera or the results' words change.
/// </summary>
public partial class CenterBanner : HudPanel
{
    private const int CaptionSize = 28, NumberSize = 96, SuffixSize = 24, BarSize = 24;
    private const float CaptionBaseline = 48f, NumberBaseline = 150f, SuffixBaseline = 192f;   // inside the plate, from its top
    private const int PlateRadius = 24, BarRadius = 18;
    private const float PlateAlpha = 0.8f;
    private const string Caption = "你被击毁了", Suffix = "秒后复活";
    private const int ResultsTitleSize = 84, ResultsSubtitleSize = 32, ResultsFooterSize = 26;
    private const float DimAlpha = 0.55f;

    /// <summary>What the results words are made from; they are rebuilt when this changes.</summary>
    private readonly record struct ResultsKey(BattleMode Mode, byte OwnSlot, byte OwnTeam, byte WinnerTeam, byte WinnerSlot, PlateValues Plate, byte OwnRank, bool WinnerAlive, string? WinnerName,
        MatchEndReason Reason);

    private readonly StyleBoxFlat _plate = HudStyle.RoundedBox(new Color(HudStyle.Panel, PlateAlpha), PlateRadius, 8);
    private readonly StyleBoxFlat _bar = HudStyle.RoundedBox(new Color(HudStyle.Panel, PlateAlpha), BarRadius, 8);
    private BannerKind _kind;
    private int _seconds = -1;
    private string _number = "";
    private string? _target;
    private bool _free;
    private string _barText = "";
    private ResultsKey _resultsKey;
    private ResultsParts _results;
    private int _footerSeconds = -1;
    private bool _footerHost;
    private string _footer = "";

    public void Update(in MatchHudFrame frame)
    {
        if (frame.Banner == BannerKind.Results)
        {
            UpdateResults(frame);
            return;
        }
        int seconds = frame.Banner == BannerKind.Respawn ? HudText.CeilSeconds(frame.RespawnIn) : 0;
        string? target = frame.Banner == BannerKind.None ? null : frame.SpectateTarget;
        bool free = frame.Banner != BannerKind.None && target is null && frame.SpectateFree;
        if (frame.Banner == _kind && seconds == _seconds && target == _target && free == _free) return;
        if (seconds != _seconds) _number = HudText.RespawnNumber(frame.RespawnIn);
        if (free) _barText = HudText.SpectateFreeLine;
        else if (target != _target && target is not null) _barText = HudText.SpectateLine(target);
        (_kind, _seconds, _target, _free) = (frame.Banner, seconds, target, free);
        QueueRedraw();
    }

    private void UpdateResults(in MatchHudFrame frame)
    {
        bool changed = _kind != BannerKind.Results;
        _kind = BannerKind.Results;
        var key = new ResultsKey(frame.Mode, frame.OwnSlot, frame.OwnTeam, frame.Results.WinnerTeam, frame.Results.WinnerSlot, frame.Plate,
            frame.OwnSlot < frame.Players.Length ? frame.Players[frame.OwnSlot].Rank : (byte)0,
            frame.Results.WinnerSlot < frame.Players.Length && frame.Players[frame.Results.WinnerSlot].Alive,
            frame.Results.WinnerSlot < frame.Names.Length ? frame.Names[frame.Results.WinnerSlot] : null,
            frame.Results.Reason);
        if (changed || key != _resultsKey)
        {
            _resultsKey = key;
            _results = HudText.Results(frame.Mode, frame.OwnSlot, frame.OwnTeam, frame.Results, frame.Plate, frame.Players, frame.Names);
            changed = true;
        }
        int seconds = HudText.CeilSeconds(frame.Results.TicksLeft);
        if (changed || seconds != _footerSeconds || frame.Results.IsHost != _footerHost)
        {
            (_footerSeconds, _footerHost) = (seconds, frame.Results.IsHost);
            _footer = HudText.ReturnLine(frame.Results.TicksLeft, frame.Results.IsHost);
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        switch (_kind)
        {
            case BannerKind.Results: DrawResults(); break;
            case BannerKind.Respawn:
                DrawRespawn();
                if (_target is not null || _free) DrawSpectate();   // waiting to respawn with the camera on a teammate
                break;
            case BannerKind.Spectate: DrawSpectate(); break;
        }
    }

    private void DrawRespawn()
    {
        HudRect box = Boxes.Banner;
        DrawStyleBox(_plate, ToRect(box));
        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + CaptionBaseline, Caption, CaptionSize, HudStyle.Text);
        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + NumberBaseline, _number, NumberSize, HudStyle.Accent, 10);
        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + SuffixBaseline, Suffix, SuffixSize, Palette.UiDim.ToColor(), 5);
    }

    private void DrawSpectate()
    {
        HudRect box = Boxes.SpectateBar;
        DrawStyleBox(_bar, ToRect(box));
        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + HudStyle.Baseline(box.Height / 2, BarSize), _barText, BarSize, HudStyle.Text, 5);
    }

    private void DrawResults()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(HudStyle.OutlineColor, DimAlpha));
        HudRect box = Boxes.Results;
        Color title = _results.Tone switch
        {
            ResultTone.Win => Palette.UiGood.ToColor(),
            ResultTone.Lose => Palette.UiDanger.ToColor(),
            _ => HudStyle.Text,
        };
        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + HudLayout.ResultsTitleBaseline, _results.Title, ResultsTitleSize, title, 12);
        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + HudLayout.ResultsSubtitleBaseline, _results.Subtitle, ResultsSubtitleSize, HudStyle.Text, 6);
        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + HudLayout.ResultsFooterBaseline, _footer, ResultsFooterSize, Palette.UiDim.ToColor(), 5);
    }
}
