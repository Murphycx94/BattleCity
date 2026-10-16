using Godot;

namespace Tank.Client.Overlay;

/// <summary>
/// Cartoon capsule health bars floating over other tanks' heads (and over damaged walls), in screen space: green, red
/// when low (<see cref="HudStyle.HealthFill"/>), with a dark outline. Shown only for bars marked shown within
/// <see cref="MaxRange"/> m of the camera and in front of it. Feed the bars every frame with <see cref="Set"/>, then they
/// draw themselves.
/// </summary>
public partial class HealthBars : Control
{
    private const float MaxRange = 250f;
    private const float Outline = 2.5f;
    private const int CornerDetail = 6;

    private float _width = 64f, _height = 11f;
    private StyleBoxFlat _back = null!;
    private HudStyle.HealthFill _fill = null!;
    private Bar[] _bars = Array.Empty<Bar>();
    private Camera3D? _camera;

    private struct Bar
    {
        public bool Shown;
        public Vector3 Head;
        public float Share;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <param name="camera">The camera the bars are projected through.</param>
    /// <param name="count">How many bars there are; <see cref="Set"/> takes an index below it.</param>
    /// <param name="width">Bar width in pixels, outline aside: 64 over tanks, smaller over walls.</param>
    /// <param name="height">Bar height in pixels, outline aside: 11 over tanks, smaller over walls.</param>
    public void Setup(Camera3D camera, int count, float width = 64f, float height = 11f)
    {
        (_camera, _width, _height) = (camera, width, height);
        _bars = new Bar[count];
        _back = HudStyle.RoundedBox(HudStyle.OutlineColor, (int)(height / 2 + Outline), CornerDetail);
        _fill = new HudStyle.HealthFill((int)(height / 2), CornerDetail);
    }

    /// <param name="head">World point the bar sits on: just above a tank, or on a wall's face.</param>
    /// <param name="share">Health left, 0 … 1.</param>
    /// <param name="shown">False for no bar (a wreck, a wall that is down or unhurt).</param>
    public void Set(int index, Vector3 head, float share, bool shown)
    {
        bool wasShown = _bars[index].Shown;
        _bars[index] = new Bar { Shown = shown, Head = head, Share = Math.Clamp(share, 0f, 1f) };
        if (shown || wasShown) QueueRedraw();   // a bar that stays hidden changes nothing on screen
    }

    public override void _Draw()
    {
        if (_camera is null) return;
        Vector3 eye = _camera.GlobalPosition;
        foreach (Bar bar in _bars)
        {
            if (!bar.Shown || bar.Head.DistanceTo(eye) > MaxRange || _camera.IsPositionBehind(bar.Head)) continue;
            Vector2 at = _camera.UnprojectPosition(bar.Head);
            if (!GetViewportRect().HasPoint(at)) continue;
            var back = new Rect2(at.X - _width / 2 - Outline, at.Y - _height / 2 - Outline, _width + 2 * Outline, _height + 2 * Outline);
            DrawStyleBox(_back, back);
            if (bar.Share <= 0f) continue;
            DrawStyleBox(_fill.For(bar.Share), new Rect2(at.X - _width / 2, at.Y - _height / 2, MathF.Max(_height, _width * bar.Share), _height));
        }
    }
}
