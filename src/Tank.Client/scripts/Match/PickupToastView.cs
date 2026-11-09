using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;

namespace Tank.Client.Match;

/// <summary>
/// The pickup toasts under the centre banner (stage 6 design §4.3): the lines of a <see cref="PickupFeed"/> inside
/// <see cref="HudBoxes.Pickup"/>, which holds all <see cref="PickupFeed.Capacity"/> of them — the newest in the bottom row,
/// each older one a row up (<see cref="HudBoxes.PickupRow"/>) — in the crate's colour, outlined and centred. A line pops in
/// over its first <see cref="PopupCurves.PickupSettle"/> s (<see cref="PopupCurves.PickupScale"/>, scaled about its own
/// centre) and fades over its last <see cref="PopupCurves.FadeTime"/> s (<see cref="PopupCurves.Alpha"/>). The words and
/// their widths are built only when the feed's revision changes; while any line shows the view redraws every frame (it is
/// popping or fading), and with none it draws nothing — so the other modes' screens are untouched.
/// </summary>
public partial class PickupToastView : HudPanel
{
    private const int TextSize = 28, TextOutline = 4;

    private readonly PickupFeed _feed;
    private readonly string[] _texts = new string[PickupFeed.Capacity];
    private readonly float[] _widths = new float[PickupFeed.Capacity];
    private readonly Color[] _colors = new Color[PickupFeed.Capacity];
    private readonly double[] _born = new double[PickupFeed.Capacity];
    private int _revision = -1;
    private int _count;
    private double _now;

    public PickupToastView(PickupFeed feed) => _feed = feed;

    /// <summary>Once a frame: reads the feed at <paramref name="now"/> (the scene clock, s), rebuilding the words if it changed.</summary>
    public void Update(double now)
    {
        ReadOnlySpan<PickupLine> lines = _feed.Visible(now);
        _now = now;
        if (_feed.Revision != _revision)
        {
            _revision = _feed.Revision;
            _count = lines.Length;
            for (int i = 0; i < lines.Length; i++)
            {
                _texts[i] = lines[i].Text;
                _widths[i] = HudStyle.TextWidth(_texts[i], TextSize);
                _colors[i] = Palette.Crate(lines[i].Kind).ToColor();
                _born[i] = lines[i].ExpiresAt - PickupFeed.Lifetime;
            }
            QueueRedraw();   // the last line gone: one more draw clears it
            return;
        }
        if (_count > 0) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_count == 0) return;
        float baseline = HudStyle.Baseline(0f, TextSize);
        for (int i = 0; i < _count; i++)
        {
            // A line born ahead of the clock (the gallery's, frozen at 0) is drawn settled rather than not yet popped.
            float age = (float)(_now - _born[i]);
            float scale = age < 0f ? 1f : PopupCurves.PickupScale(age);
            float alpha = PopupCurves.Alpha(age, (float)PickupFeed.Lifetime);
            HudRect row = Boxes.PickupRow(_count - 1 - i);   // the lines run oldest first: the last is the newest, at the bottom
            DrawSetTransform(new Vector2(row.CenterX, row.Y + row.Height / 2), 0f, new Vector2(scale, scale));
            HudStyle.DrawOutlined(this, new Vector2(-_widths[i] / 2, baseline), _texts[i], TextSize, new Color(_colors[i], alpha), TextOutline);
        }
        DrawSetTransform(Vector2.Zero);
    }
}
