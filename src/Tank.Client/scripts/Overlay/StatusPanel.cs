using Godot;

namespace Tank.Client.Overlay;

/// <summary>
/// Bottom-left rounded panel: a speedometer icon with the speed as a big outlined number, and a shell icon with the
/// reload state ("就绪" in the accent colour, or the seconds left). Icons are drawn with primitives, no image files.
/// Redraws only when the shown speed (whole km/h), reload (tenths of a second) or ready state changes.
/// </summary>
public partial class StatusPanel : Control
{
    private const float Margin = 24f;
    private const float PanelHeight = 92f;
    private const float PanelAlpha = 0.85f;   // more opaque than the HUD default so it stays readable under the sniper vignette
    private const float Pad = 16f;
    private const float IconColumn = 38f;
    private const float ColumnGap = 22f;
    private const int NumberSize = 40;
    private const int ReadySize = 34;
    private const int UnitSize = 20;

    // Shell icon, centred on the origin; the outline is the same polygon scaled up. Built once.
    private static readonly Vector2[] ShellBody =
    {
        new(-7, 18), new(7, 18), new(7, -4), new(4, -13), new(0, -18), new(-4, -13), new(-7, -4),
    };
    private static readonly Vector2[] ShellOutline = ScaleAll(ShellBody, 1.22f);

    private readonly StyleBoxFlat _box = new()
    {
        BgColor = new Color(HudStyle.Panel, PanelAlpha),
        CornerDetail = 8,
        AntiAliasing = true,
    };

    private int _speed;
    private int _reloadTenths;
    private bool _ready = true;
    private string _speedText = "0";
    private string _reloadText = "就绪";
    private float _panelWidth;
    private float _shellOffset;   // x of the shell column relative to the speedometer centre

    public override void _Ready()
    {
        _box.SetCornerRadiusAll(18);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        // Sized for the widest content ("888 km/h", "8.8 秒") so the panel never resizes while playing.
        float speedW = HudStyle.TextWidth("888", NumberSize) + 6f + HudStyle.TextWidth("km/h", UnitSize);
        float reloadW = HudStyle.TextWidth("8.8", NumberSize) + 6f + HudStyle.TextWidth("秒", UnitSize);
        _shellOffset = 28f - 14f + HudStyle.TextWidth("888", NumberSize) + 6f + HudStyle.TextWidth("km/h", UnitSize) + ColumnGap + 14f;
        _panelWidth = IconColumn + 28f - 14f + speedW + ColumnGap + 20f + 20f + reloadW + Pad;
    }

    /// <param name="speedKmh">Ground speed, km/h (sign ignored).</param>
    /// <param name="reloadSeconds">Seconds until the gun is ready; 0 = ready.</param>
    public void UpdateState(float speedKmh, float reloadSeconds)
    {
        int speed = (int)MathF.Round(MathF.Abs(speedKmh));
        bool ready = reloadSeconds <= 0f;
        int tenths = ready ? 0 : (int)MathF.Round(reloadSeconds * 10f);
        if (speed == _speed && ready == _ready && tenths == _reloadTenths) return;
        if (speed != _speed) _speedText = speed.ToString();
        if (tenths != _reloadTenths || ready != _ready) _reloadText = ready ? "就绪" : $"{tenths / 10}.{tenths % 10}";
        _speed = speed;
        _ready = ready;
        _reloadTenths = tenths;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var origin = new Vector2(Margin, Size.Y - Margin - PanelHeight);
        DrawStyleBox(_box, new Rect2(origin, new Vector2(_panelWidth, PanelHeight)));
        float baseline = origin.Y + PanelHeight / 2 + NumberSize * 0.36f;

        Vector2 gauge = origin + new Vector2(IconColumn, PanelHeight / 2);
        DrawSpeedometer(gauge);
        float x = gauge.X + 28f;
        HudStyle.DrawOutlined(this, new Vector2(x, baseline), _speedText, NumberSize, HudStyle.Text);
        x += HudStyle.TextWidth(_speedText, NumberSize) + 6f;
        HudStyle.DrawOutlined(this, new Vector2(x, baseline), "km/h", UnitSize, HudStyle.Text, 4);

        // The reload column starts after the widest possible speed text, so it doesn't move as the speed changes.
        Vector2 shell = new(gauge.X + _shellOffset, origin.Y + PanelHeight / 2);
        DrawShell(shell, _ready ? HudStyle.Accent : HudStyle.Text);
        x = shell.X + 20f;
        int size = _ready ? ReadySize : NumberSize;
        HudStyle.DrawOutlined(this, new Vector2(x, baseline), _reloadText, size, _ready ? HudStyle.Accent : HudStyle.Text);
        if (!_ready) HudStyle.DrawOutlined(this, new Vector2(x + HudStyle.TextWidth(_reloadText, NumberSize) + 6f, baseline), "秒", UnitSize, HudStyle.Text, 4);
    }

    /// <summary>A half-dial with a needle.</summary>
    private void DrawSpeedometer(Vector2 c)
    {
        const float R = 17f;
        HudStyle.DrawOutlinedArc(this, c + new Vector2(0, 6), R, Mathf.Pi, Mathf.Tau, HudStyle.Text, 4f);
        Vector2 tip = c + new Vector2(0, 6) + new Vector2(MathF.Cos(-Mathf.Pi * 0.3f), MathF.Sin(-Mathf.Pi * 0.3f)) * (R - 4f);
        DrawLine(c + new Vector2(0, 6), tip, HudStyle.OutlineColor, 7f, true);
        DrawLine(c + new Vector2(0, 6), tip, HudStyle.Accent, 3.5f, true);
        DrawCircle(c + new Vector2(0, 6), 4f, HudStyle.Text, true, -1f, true);
    }

    /// <summary>An upright shell: a rounded case with a pointed nose.</summary>
    private void DrawShell(Vector2 c, Color color)
    {
        DrawSetTransform(c);
        DrawColoredPolygon(ShellOutline, HudStyle.OutlineColor);
        DrawColoredPolygon(ShellBody, color);
        DrawLine(new Vector2(-7, 10), new Vector2(7, 10), HudStyle.OutlineColor, 2f, true);
        DrawSetTransform(Vector2.Zero);
    }

    private static Vector2[] ScaleAll(Vector2[] points, float factor)
    {
        var scaled = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++) scaled[i] = points[i] * factor;
        return scaled;
    }
}
