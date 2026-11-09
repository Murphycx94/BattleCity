using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The zone line under the mode label in a royale (stage 6 design §3.3), where a standard battle's capture bars go
/// (<see cref="HudBoxes.ZoneLine"/>): a rounded dark box with "毒圈 2/5 · 0:45 后缩小", "毒圈缩小中 0:30" or "毒圈已闭合"
/// centred on it (<see cref="ZoneText.Line"/>), the words in the wall's blue while the ring shrinks and in the plain text
/// colour otherwise. Nothing outside a royale. The words are rebuilt, and the box redrawn, only when the part of the
/// <see cref="ZoneHud"/> they show has changed; the outside warning's part (<see cref="ZoneHud.Outside"/>,
/// <see cref="ZoneHud.Percent"/>) is <see cref="ZoneWarningView"/>'s and is ignored here.
/// </summary>
public partial class ZoneLineView : HudPanel
{
    private const int TextSize = 18, TextOutline = 4;
    private const int BoxRadius = 13, CornerDetail = 6;
    private const float BoxAlpha = 0.7f;

    private static readonly Color ShrinkColor = Palette.ZoneWall.ToColor();

    private readonly StyleBoxFlat _box = HudStyle.RoundedBox(Palette.UiPanel.ToColor(BoxAlpha), BoxRadius, CornerDetail);
    private ZoneHud _zone;
    private string _line = string.Empty;

    /// <summary>Call once a frame, shown or not.</summary>
    public void Update(in MatchHudFrame frame)
    {
        ZoneHud zone = frame.Zone with { Outside = false, Percent = 0 };   // only what the line shows
        if (zone == _zone) return;
        _zone = zone;
        _line = ZoneText.Line(zone);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_zone.On) return;
        HudRect box = Boxes.ZoneLine;
        DrawStyleBox(_box, ToRect(box));
        Color color = _zone.Phase == ZonePhase.Shrinking ? ShrinkColor : HudStyle.Text;
        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + HudStyle.Baseline(box.Height / 2, TextSize), _line, TextSize, color, TextOutline);
    }
}
