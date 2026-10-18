using Godot;
using Tank.Client.Art;
using Tank.Sim.Vision;

namespace Tank.Client.Overlay;

/// <summary>
/// The spotting debug display: one <see cref="SpotText"/> line over each enemy's head, above its health bar, in white
/// while the tank is drawn and faded grey while it is hidden (hidden tanks get a line too, to check the rule against).
/// Shown only within <see cref="MaxRange"/> m of the camera and in front of it. <see cref="Place"/> every frame follows
/// the heads; <see cref="Set"/>, once per simulation tick is enough, gives the looks, and a line's text is rebuilt only
/// when what it shows changes.
/// </summary>
public partial class SpotLabels : Control
{
    private const float MaxRange = 400f;
    private const int TextSize = 18;
    private const float Above = 26f;        // pixels from the head point up to the line's middle, clear of the health bar
    private const float HiddenAlpha = 0.55f;

    private Line[] _lines = Array.Empty<Line>();
    private Camera3D? _camera;

    private struct Line
    {
        public bool HasLine;
        public Vector3 Head;
        public SpotLine Shown;
        public bool Drawn;     // the tank is drawn: seen now, or lately
        public string? Text;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Setup(Camera3D camera, int count)
    {
        _camera = camera;
        _lines = new Line[count];
    }

    /// <param name="head">The point over the tank the line sits above.</param>
    /// <param name="hasLine">False for no line (a wreck).</param>
    public void Place(int index, Vector3 head, bool hasLine)
    {
        (_lines[index].Head, _lines[index].HasLine) = (head, hasLine);
        QueueRedraw();
    }

    /// <param name="drawn">The tank is drawn: seen now, or lately.</param>
    public void Set(int index, in Sighting sighting, bool drawn)
    {
        ref Line line = ref _lines[index];
        SpotLine shown = SpotText.Of(sighting, lingering: drawn);
        if (line.Text is null || shown != line.Shown) line.Text = SpotText.Format(shown);
        (line.Shown, line.Drawn) = (shown, drawn);
    }

    public override void _Draw()
    {
        if (_camera is null) return;
        Vector3 eye = _camera.GlobalPosition;
        foreach (Line line in _lines)
        {
            if (!line.HasLine || line.Text is null || line.Head.DistanceTo(eye) > MaxRange || _camera.IsPositionBehind(line.Head)) continue;
            Vector2 at = _camera.UnprojectPosition(line.Head);
            if (!GetViewportRect().HasPoint(at)) continue;
            Color color = line.Drawn ? HudStyle.Text : new Color(Palette.HitBlocked.ToColor(), HiddenAlpha);
            HudStyle.DrawOutlinedCentered(this, at.X, HudStyle.Baseline(at.Y - Above, TextSize), line.Text, TextSize, color, 4);
        }
    }
}
