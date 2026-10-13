using Godot;

namespace Tank.Client.Overlay;

/// <summary>
/// Centre dot = where the player wants to aim; ring = where the gun would hit right now (with a reload arc).
/// When they overlap the gun is on target. A status line sits bottom-left.
/// </summary>
public partial class Reticle : Control
{
    private const float RingRadius = 16f;
    private const float ReloadRadius = 22f;
    private static readonly Color Ring = new(1f, 1f, 1f, 0.9f);
    private static readonly Color Dot = new(1f, 1f, 1f, 0.7f);
    private static readonly Color ReloadColor = new(1f, 0.72f, 0.18f);

    private Vector2? _impact;
    private float _reload = 1f;
    private string _status = "";

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <param name="impact">Screen position of the predicted hit, or null if it is behind the camera.</param>
    /// <param name="reloadProgress">0 just fired … 1 ready.</param>
    public void UpdateState(Vector2? impact, float reloadProgress, string status)
    {
        _impact = impact;
        _reload = Mathf.Clamp(reloadProgress, 0f, 1f);
        _status = status;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawCircle(Size / 2, 2.5f, Dot);
        if (_impact is { } p)
        {
            DrawArc(p, RingRadius, 0, Mathf.Tau, 40, Ring, 2f, true);
            if (_reload < 1f)
                DrawArc(p, ReloadRadius, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * _reload, 40, ReloadColor, 3f, true);
        }
        DrawString(ThemeDB.FallbackFont, new Vector2(24, Size.Y - 28), _status, HorizontalAlignment.Left, -1, 18, Colors.White);
    }
}
