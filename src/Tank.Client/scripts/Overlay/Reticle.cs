using Godot;

namespace Tank.Client.Overlay;

/// <summary>
/// Centre dot = where the player wants to aim; thick ring = where the gun would hit right now, with a reload ring in the
/// accent colour around it that pops (1 → 1.25 → 1 in 0.2 s) when the gun is ready; a thin circle around the same point
/// = the aim circle (where the shell may land), true to scale, so it is small in the third-person view and grows in
/// the sniper sight and while moving. In sniper mode the zoom shows below the centre. Every mark has a dark outline.
/// </summary>
public partial class Reticle : Control
{
    private const float RingRadius = 17f;
    private const float RingWidth = 3f;
    private const float ReloadRadius = 25f;
    private const float ReloadWidth = 4f;
    private const float DotRadius = 3f;
    private const float PopSeconds = 0.2f;
    private const float PopScale = 0.25f;
    private const float AimCircleWidth = 2f;
    private const float MinAimCircle = 1.5f;   // smaller than this it would just be a blob on the dot

    private Vector2? _impact;
    private float _reload = 1f;
    private float _zoom;
    private float _aimCircle;
    private float _popLeft;
    private string _zoomText = "";

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <param name="impact">Screen position of the predicted hit, or null if it is behind the camera.</param>
    /// <param name="reloadProgress">0 just fired … 1 ready.</param>
    /// <param name="zoom">Sniper magnification, or 0 outside sniper mode.</param>
    /// <param name="aimCircle">Aim-circle radius on screen, pixels.</param>
    public void UpdateState(Vector2? impact, float reloadProgress, float zoom, float aimCircle)
    {
        _aimCircle = aimCircle;
        float reload = Mathf.Clamp(reloadProgress, 0f, 1f);
        if (_reload < 1f && reload >= 1f) _popLeft = PopSeconds;
        _impact = impact;
        _reload = reload;
        if (zoom != _zoom) _zoomText = $"{zoom:0}×";
        _zoom = zoom;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_popLeft <= 0f) return;
        _popLeft = MathF.Max(0f, _popLeft - (float)delta);
        QueueRedraw();   // keep animating the ready pop
    }

    public override void _Draw()
    {
        Vector2 center = Size / 2;
        DrawCircle(center, DotRadius + 2f, HudStyle.OutlineColor, true, -1f, true);
        DrawCircle(center, DotRadius, HudStyle.Text, true, -1f, true);
        if (_zoom > 0) HudStyle.DrawOutlinedCentered(this, center.X, center.Y + 64f, _zoomText, 26, HudStyle.Text);
        if (_impact is not { } p) return;

        if (_aimCircle >= MinAimCircle)
            HudStyle.DrawOutlinedArc(this, p, _aimCircle, 0, Mathf.Tau, HudStyle.Accent, AimCircleWidth, 1.5f);
        float pop = 1f + PopScale * MathF.Sin(MathF.PI * (1f - _popLeft / PopSeconds)) * (_popLeft > 0 ? 1f : 0f);
        HudStyle.DrawOutlinedArc(this, p, RingRadius * pop, 0, Mathf.Tau, HudStyle.Text, RingWidth);
        if (_reload < 1f)
            HudStyle.DrawOutlinedArc(this, p, ReloadRadius, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * _reload, HudStyle.Accent, ReloadWidth);
        else if (_popLeft > 0)
            HudStyle.DrawOutlinedArc(this, p, ReloadRadius * pop, 0, Mathf.Tau, HudStyle.Accent, ReloadWidth);
    }
}
