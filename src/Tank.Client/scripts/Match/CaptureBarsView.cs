using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The capture bars under the plate and the mode's name (stage 5 design §3.4): one row per base with points, the own
/// base first, as wide as the plate. Each row is a rounded dark box filled from the left in proportion to the points
/// (of 100), with "我方基地 37" / "敌方基地 62" on it. The fill's kind and colour are <see cref="MatchColors.FillOf"/> and
/// <see cref="MatchColors.FillColor"/>: the own base's is red, and blinks (<see cref="CaptureAlarm.IsDarkHalf"/>, between
/// full and <see cref="DimAlpha"/>, on the scene's clock) while <see cref="CaptureAlarm"/> says its points are rising;
/// the enemy base's is the ally blue-grey — it is us taking it — and never blinks. Redraws only when the bars,
/// their number or the blink's phase change; the words are rebuilt only when a bar changed.
/// </summary>
public partial class CaptureBarsView : HudPanel
{
    private const int LabelSize = 18, LabelOutline = 4;
    private const float LabelInset = 12f;
    private const int BoxRadius = 13, FillRadius = 10, CornerDetail = 6;
    private const float BoxAlpha = 0.85f, FillInset = 3f;
    private const float DimAlpha = 0.35f;
    private const byte MaxPoints = BaseCapture.PointsToWin;

    private readonly StyleBoxFlat _box = HudStyle.RoundedBox(new Color(HudStyle.Panel, BoxAlpha), BoxRadius, CornerDetail);
    private readonly StyleBoxFlat[] _fills =   // by CaptureFill
    {
        Fill(CaptureFill.Danger, 1f),
        Fill(CaptureFill.DangerDim, DimAlpha),
        Fill(CaptureFill.Ally, 1f),
    };
    private readonly CaptureAlarm _alarm = new();
    private readonly string[] _labels = { "", "" };
    private CaptureBars _bars;
    private bool _dim;   // the own bar's fill is in the dark half of its blink

    /// <summary>Call once a frame, shown or not (the alarm follows the own base's points either way); <paramref name="now"/> is the scene's clock in seconds.</summary>
    public void Update(in MatchHudFrame frame, double now)
    {
        CaptureBars bars = frame.Capture;
        bool dim = _alarm.Update(bars, now) && CaptureAlarm.IsDarkHalf(now);
        bool changed = dim != _dim;
        _dim = dim;
        if (bars != _bars)
        {
            for (int i = 0; i < bars.Count; i++)
                if (i >= _bars.Count || bars[i] != _bars[i]) _labels[i] = CaptureText.Label(bars[i]);
            _bars = bars;
            changed = true;
        }
        if (changed) QueueRedraw();
    }

    private static StyleBoxFlat Fill(CaptureFill kind, float alpha) =>
        HudStyle.RoundedBox(MatchColors.FillColor(kind).ToColor(alpha), FillRadius, CornerDetail);

    public override void _Draw()
    {
        for (int i = 0; i < _bars.Count; i++) DrawBar(Boxes.CaptureRow(i), _bars[i], _labels[i]);
    }

    private void DrawBar(HudRect row, in CaptureBar bar, string label)
    {
        DrawStyleBox(_box, ToRect(row));
        float inner = row.Width - 2 * FillInset;
        float width = inner * Math.Min(bar.Points, MaxPoints) / MaxPoints;
        if (width > 0)
        {
            DrawStyleBox(_fills[(int)MatchColors.FillOf(bar, _dim)], new Rect2(row.X + FillInset, row.Y + FillInset, width, row.Height - 2 * FillInset));
        }
        HudStyle.DrawOutlined(this, new Vector2(row.X + LabelInset, row.Y + HudStyle.Baseline(row.Height / 2, LabelSize)), label, LabelSize, HudStyle.Text, LabelOutline);
    }
}
