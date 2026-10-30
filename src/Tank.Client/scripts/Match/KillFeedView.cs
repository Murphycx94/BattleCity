using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;

namespace Tank.Client.Match;

/// <summary>
/// The kill feed at the top right (blueprint §4.2): up to five rows, oldest at the top, each a rounded plate right
/// against the margin with "killer 击毁 victim" in the sides' colours (a hidden shooter gives "victim 被击毁", a kill
/// within one side "killer 误伤 victim", a tank the zone finished "victim 死于毒圈" with the tail in the wall's blue). A row the player took part in gets a tint. Each name is cut to
/// <see cref="NameWidth"/> with "…", so even two 24-byte names keep a row inside <see cref="HudLayout.KillWidth"/>, clear
/// of the score plate. Rows expire in <see cref="KillFeed"/>; this view redraws only when the feed's revision changes.
/// </summary>
public partial class KillFeedView : HudPanel
{
    private const int TextSize = 22, TextOutline = 4;
    private const float PadX = 14f, WordGap = 8f;
    private const int RowRadius = 12;
    private const float RowAlpha = 0.7f;
    private const float TintAlpha = 0.45f;

    // Two names, the two-character verb (about 44 at 22 px), two gaps and the padding: 2 × 150 + 44 + 16 + 28 = 388 < 440.
    private const float NameWidth = 150f;

    private static readonly Func<string, float> Measure = text => HudStyle.TextWidth(text, TextSize);   // cached: one delegate, not one per name

    private static readonly Color ZoneColor = Palette.ZoneWall.ToColor();

    private struct Row
    {
        public string? Killer, Verb, Victim, Tail;
        public Color KillerColor, VictimColor, TailColor;
        public RowKind Kind;
        public float Width, KillerWidth, VerbWidth, VictimWidth;
    }

    private enum RowKind : byte { Plain, Mine, Died }

    private readonly KillFeed _feed;
    private readonly Row[] _rows = new Row[KillFeed.Capacity];
    private readonly StyleBoxFlat _plain = HudStyle.RoundedBox(new Color(HudStyle.Panel, RowAlpha), RowRadius, 6);
    private readonly StyleBoxFlat _own = HudStyle.RoundedBox(new Color(HudStyle.Accent, TintAlpha), RowRadius, 6);
    private readonly StyleBoxFlat _died = HudStyle.RoundedBox(new Color(Palette.UiDanger.ToColor(), TintAlpha), RowRadius, 6);
    private int _revision = -1;
    private int _count;

    public KillFeedView(KillFeed feed) => _feed = feed;

    /// <summary>Reads the feed at <paramref name="now"/>; the rows are rebuilt (and redrawn) only if it changed.</summary>
    public void Update(double now)
    {
        ReadOnlySpan<FeedLine> lines = _feed.Visible(now);
        if (_feed.Revision == _revision) return;
        _revision = _feed.Revision;
        _count = lines.Length;
        for (int i = 0; i < lines.Length; i++) Fill(ref _rows[i], lines[i]);
        QueueRedraw();
    }

    private static void Fill(ref Row row, in FeedLine line)
    {
        bool hidden = line.Zone || line.Killer is null;   // no shooter to name: the zone's rows and the hidden shooter's alike
        row.Killer = hidden ? null : HudText.Fit(line.Killer!, NameWidth, Measure);
        row.Verb = hidden ? null : line.Friendly ? FeedLine.FriendlyVerb : FeedLine.KilledVerb;
        row.Victim = HudText.Fit(line.Victim, NameWidth, Measure);
        row.Tail = line.Zone ? FeedLine.ZoneTail : hidden ? FeedLine.HiddenShooterTail : null;
        row.KillerColor = SideColor(line.KillerSide);
        row.VictimColor = SideColor(line.VictimSide);
        row.TailColor = line.Zone ? ZoneColor : HudStyle.Text;
        row.KillerWidth = hidden ? 0f : HudStyle.TextWidth(row.Killer!, TextSize) + WordGap;
        row.VerbWidth = hidden ? 0f : HudStyle.TextWidth(row.Verb!, TextSize) + WordGap;
        row.VictimWidth = HudStyle.TextWidth(row.Victim, TextSize);
        row.Width = 2 * PadX + row.KillerWidth + row.VerbWidth + row.VictimWidth + (row.Tail is null ? 0f : WordGap + HudStyle.TextWidth(row.Tail, TextSize));
        row.Kind = !line.Zone && line.KillerSide == Side.Own ? RowKind.Mine : line.VictimSide == Side.Own ? RowKind.Died : RowKind.Plain;
    }

    public override void _Draw()
    {
        for (int i = 0; i < _count; i++)
        {
            ref Row row = ref _rows[i];
            HudRect slot = Boxes.KillRow(i);
            float x = slot.Right - row.Width;
            DrawStyleBox(row.Kind switch { RowKind.Mine => _own, RowKind.Died => _died, _ => _plain }, new Rect2(x, slot.Y, row.Width, slot.Height));
            float baseline = slot.Y + HudStyle.Baseline(slot.Height / 2, TextSize);
            x += PadX;
            if (row.Killer is not null)
            {
                HudStyle.DrawOutlined(this, new Vector2(x, baseline), row.Killer, TextSize, row.KillerColor, TextOutline);
                x += row.KillerWidth;
                HudStyle.DrawOutlined(this, new Vector2(x, baseline), row.Verb!, TextSize, HudStyle.Text, TextOutline);
                x += row.VerbWidth;
            }
            HudStyle.DrawOutlined(this, new Vector2(x, baseline), row.Victim!, TextSize, row.VictimColor, TextOutline);
            if (row.Tail is null) continue;
            x += row.VictimWidth + WordGap;
            HudStyle.DrawOutlined(this, new Vector2(x, baseline), row.Tail, TextSize, row.TailColor, TextOutline);
        }
    }

    private static Color SideColor(Side side) => MatchColors.Ui(side).ToColor();
}
