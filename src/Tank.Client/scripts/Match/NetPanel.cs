using Godot;
using Tank.Client.Overlay;

namespace Tank.Client.Match;

/// <summary>
/// The F3 panel at the top left (blueprint §4.2): a title and the lines of <see cref="NetPanelText"/> in a dark plate.
/// The text is rebuilt at most <see cref="RefreshSeconds"/> apart and only while the panel is open, and it redraws only
/// when the text changed.
/// </summary>
public partial class NetPanel : HudPanel
{
    private const double RefreshSeconds = 0.25;
    private const int TitleSize = 22, LineSize = 20;
    private const float Pad = 16f, TitleBaseline = 30f, FirstLine = 62f, LineStep = 24f;
    private const int PanelRadius = 16;
    private const string Title = "网络 · F3";

    private readonly StyleBoxFlat _box = HudStyle.RoundedBox(new Color(HudStyle.Panel, 0.85f), PanelRadius, 8);
    private readonly string[] _lines = new string[NetPanelText.MaxLines];
    private double _builtAt = double.NegativeInfinity;
    private int _count;

    /// <summary>Takes a fresh sample if the last one is old enough; call every frame while the panel is open.</summary>
    public void Update(in NetSample sample, double now)
    {
        if (now - _builtAt < RefreshSeconds) return;
        _builtAt = now;
        _count = NetPanelText.Lines(sample, _lines);
        QueueRedraw();
    }

    /// <summary>The panel closed: the next time it opens it takes a sample at once.</summary>
    public void Reset() => _builtAt = double.NegativeInfinity;

    public override void _Draw()
    {
        if (_count == 0) return;
        HudRect box = Boxes.NetPanel;
        DrawStyleBox(_box, ToRect(box));
        HudStyle.DrawOutlined(this, new Vector2(box.X + Pad, box.Y + TitleBaseline), Title, TitleSize, HudStyle.Accent, 4);
        for (int i = 0; i < _count; i++)
            HudStyle.DrawOutlined(this, new Vector2(box.X + Pad, box.Y + FirstLine + i * LineStep), _lines[i], LineSize, HudStyle.Text, 4);
    }
}
