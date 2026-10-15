using Godot;
using Tank.Client.Art;

namespace Tank.Client.Overlay;

/// <summary>
/// Cartoon capsule health bars floating over other tanks' heads, in screen space: green, red below 30%, with a dark
/// outline. Shown only for live tanks within <see cref="MaxRange"/> m of the camera and in front of it (full health too).
/// Feed the bars every frame with <see cref="Set"/>, then they draw themselves.
/// </summary>
public partial class HealthBars : Control
{
    public const float MaxRange = 250f;
    private const float Width = 64f, Height = 11f, Outline = 2.5f;
    private const float LowHealth = 0.3f;

    private readonly StyleBoxFlat _back = new() { BgColor = HudStyle.OutlineColor, CornerDetail = 6, AntiAliasing = true };
    private readonly StyleBoxFlat _fill = new() { CornerDetail = 6, AntiAliasing = true };
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
        _back.SetCornerRadiusAll((int)(Height / 2 + Outline));
        _fill.SetCornerRadiusAll((int)(Height / 2));
    }

    public void Setup(Camera3D camera, int count)
    {
        _camera = camera;
        _bars = new Bar[count];
    }

    /// <param name="head">World point just above the tank, where the bar sits.</param>
    /// <param name="share">Health left, 0 … 1.</param>
    /// <param name="alive">False for a wreck: no bar.</param>
    public void Set(int index, Vector3 head, float share, bool alive)
    {
        _bars[index] = new Bar { Shown = alive, Head = head, Share = Math.Clamp(share, 0f, 1f) };
        QueueRedraw();
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
            var back = new Rect2(at.X - Width / 2 - Outline, at.Y - Height / 2 - Outline, Width + 2 * Outline, Height + 2 * Outline);
            DrawStyleBox(_back, back);
            if (bar.Share <= 0f) continue;
            _fill.BgColor = bar.Share > LowHealth ? Palette.Health.ToColor() : Palette.HitPierced.ToColor();
            DrawStyleBox(_fill, new Rect2(at.X - Width / 2, at.Y - Height / 2, MathF.Max(Height, Width * bar.Share), Height));
        }
    }
}
