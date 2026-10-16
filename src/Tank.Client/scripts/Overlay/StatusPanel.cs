using Godot;
using Tank.Client.Art;
using Tank.Sim.Combat;

namespace Tank.Client.Overlay;

/// <summary>
/// Bottom-left rounded panel in three rows: a capsule health bar with the hit points; a speedometer icon with the speed
/// as a big outlined number and a shell icon with the reload state ("就绪" in the accent colour, or the seconds left);
/// and two ammo chips, "1 穿甲 40" and "2 高爆 15", the loaded one outlined in the accent colour, an empty one greyed.
/// The vehicle's name ("中型坦克") sits just above the panel. Icons are drawn with primitives, no image files. Redraws
/// only when something it shows changes.
/// </summary>
public partial class StatusPanel : Control
{
    private const float Margin = 24f;
    private const float PanelAlpha = 0.85f;   // more opaque than the HUD default so it stays readable under the sniper vignette
    private const float Pad = 16f;
    private const float IconColumn = 38f;
    private const float ColumnGap = 22f;
    private const int NumberSize = 40;
    private const int ReadySize = 34;
    private const int UnitSize = 20;
    private const int HealthSize = 22;
    private const int ChipSize = 22;
    private const int NameSize = 24;
    private const float NameGap = 10f;   // between the name's baseline and the panel's top
    private const float HealthRow = 34f, GaugeRow = 64f, ChipRow = 40f, RowGap = 4f;
    private const float BarHeight = 16f;
    private const float ChipPad = 10f, ChipGap = 12f;
    private const int ChipRadius = 10;
    private const float PanelHeight = Pad + HealthRow + RowGap + GaugeRow + RowGap + ChipRow + Pad;

    // Shell icon, centred on the origin; the outline is the same polygon scaled up. Built once.
    private static readonly Vector2[] ShellBody =
    {
        new(-7, 18), new(7, 18), new(7, -4), new(4, -13), new(0, -18), new(-4, -13), new(-7, -4),
    };
    private static readonly Vector2[] ShellOutline = ScaleAll(ShellBody, 1.22f);
    private static readonly Vector2[] Heart = HeartShape();
    private static readonly Vector2[] HeartOutline = ScaleAll(Heart, 1.3f);

    private readonly StyleBoxFlat _box = HudStyle.RoundedBox(new Color(HudStyle.Panel, PanelAlpha), 18, 8);
    private readonly StyleBoxFlat _barBack = HudStyle.RoundedBox(HudStyle.OutlineColor, (int)(BarHeight / 2 + 2), 8);
    private readonly HudStyle.HealthFill _barFill = new((int)(BarHeight / 2), 8);
    private readonly StyleBoxFlat _chip = HudStyle.RoundedBox(new Color(HudStyle.OutlineColor, 0.6f), ChipRadius, 6);
    private readonly StyleBoxFlat _chipLoaded = LoadedChip();

    private int _speed;
    private int _reloadTenths;
    private bool _ready = true;
    private string _speedText = "0";
    private string _reloadText = "就绪";
    private int _health = -1, _maxHealth = 1;
    private string _healthText = "";
    private string _vehicle = "";
    private AmmoType _ammo;
    private int _apLeft = -1, _heLeft = -1;
    private string _apText = "", _heText = "";
    private float _panelWidth;
    private float _shellOffset;   // x of the shell column relative to the speedometer centre
    private float _chipWidth;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        // Sized for the widest content ("888 km/h", "8.8 秒", "2 高爆 88") so the panel never resizes while playing.
        float speedW = HudStyle.TextWidth("888", NumberSize) + 6f + HudStyle.TextWidth("km/h", UnitSize);
        float reloadW = HudStyle.TextWidth("8.8", NumberSize) + 6f + HudStyle.TextWidth("秒", UnitSize);
        _shellOffset = 28f - 14f + speedW + ColumnGap + 14f;
        _panelWidth = IconColumn + 28f - 14f + speedW + ColumnGap + 20f + 20f + reloadW + Pad;
        _chipWidth = HudStyle.TextWidth("2 高爆 88", ChipSize) + 2 * ChipPad;
    }

    /// <summary>The vehicle name drawn above the panel; redraws only when it changes.</summary>
    public void SetVehicle(string name)
    {
        if (name == _vehicle) return;
        _vehicle = name;
        QueueRedraw();
    }

    /// <param name="speedKmh">Ground speed, km/h (sign ignored).</param>
    /// <param name="reloadSeconds">Seconds until the gun is ready; 0 = ready.</param>
    public void UpdateState(float speedKmh, float reloadSeconds, int health, int maxHealth, AmmoType ammo, int apLeft, int heLeft)
    {
        int speed = (int)MathF.Round(MathF.Abs(speedKmh));
        bool ready = reloadSeconds <= 0f;
        int tenths = ready ? 0 : (int)MathF.Round(reloadSeconds * 10f);
        if (speed == _speed && ready == _ready && tenths == _reloadTenths && health == _health && maxHealth == _maxHealth
            && ammo == _ammo && apLeft == _apLeft && heLeft == _heLeft) return;
        if (speed != _speed) _speedText = speed.ToString();
        if (tenths != _reloadTenths || ready != _ready) _reloadText = ready ? "就绪" : $"{tenths / 10}.{tenths % 10}";
        if (health != _health) _healthText = health.ToString();
        if (apLeft != _apLeft) _apText = $"1 穿甲 {apLeft}";
        if (heLeft != _heLeft) _heText = $"2 高爆 {heLeft}";
        (_speed, _ready, _reloadTenths, _health, _maxHealth) = (speed, ready, tenths, health, maxHealth);
        (_ammo, _apLeft, _heLeft) = (ammo, apLeft, heLeft);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var origin = new Vector2(Margin, Size.Y - Margin - PanelHeight);
        DrawStyleBox(_box, new Rect2(origin, new Vector2(_panelWidth, PanelHeight)));
        HudStyle.DrawOutlined(this, new Vector2(origin.X + Pad, origin.Y - NameGap), _vehicle, NameSize, HudStyle.Text, 4);
        float y = origin.Y + Pad;
        DrawHealth(origin.X, y + HealthRow / 2);
        y += HealthRow + RowGap;
        DrawGauges(origin.X, y + GaugeRow / 2);
        y += GaugeRow + RowGap;
        DrawChips(origin.X, y + ChipRow / 2);
    }

    private void DrawHealth(float left, float middle)
    {
        Vector2 heart = new(left + IconColumn, middle);
        DrawSetTransform(heart);
        DrawColoredPolygon(HeartOutline, HudStyle.OutlineColor);
        DrawColoredPolygon(Heart, Palette.HitPierced.ToColor());
        DrawSetTransform(Vector2.Zero);

        float numberWidth = HudStyle.TextWidth("8888", HealthSize);
        float barLeft = heart.X + 24f, barRight = left + _panelWidth - Pad - numberWidth - 10f;
        DrawStyleBox(_barBack, new Rect2(barLeft - 2, middle - BarHeight / 2 - 2, barRight - barLeft + 4, BarHeight + 4));
        float share = Math.Clamp(_health / (float)Math.Max(_maxHealth, 1), 0f, 1f);
        if (share > 0f)
        {
            float width = MathF.Max(BarHeight, (barRight - barLeft) * share);   // never narrower than its round ends
            DrawStyleBox(_barFill.For(share), new Rect2(barLeft, middle - BarHeight / 2, width, BarHeight));
        }
        HudStyle.DrawOutlined(this, new Vector2(barRight + 10f, HudStyle.Baseline(middle, HealthSize)), _healthText, HealthSize, HudStyle.Text, 4);
    }

    private void DrawGauges(float left, float middle)
    {
        float baseline = HudStyle.Baseline(middle, NumberSize);
        Vector2 gauge = new(left + IconColumn, middle);
        DrawSpeedometer(gauge);
        float x = gauge.X + 28f;
        HudStyle.DrawOutlined(this, new Vector2(x, baseline), _speedText, NumberSize, HudStyle.Text);
        x += HudStyle.TextWidth(_speedText, NumberSize) + 6f;
        HudStyle.DrawOutlined(this, new Vector2(x, baseline), "km/h", UnitSize, HudStyle.Text, 4);

        // The reload column starts after the widest possible speed text, so it doesn't move as the speed changes.
        Vector2 shell = new(gauge.X + _shellOffset, middle);
        DrawShell(shell, _ready ? HudStyle.Accent : HudStyle.Text);
        x = shell.X + 20f;
        int size = _ready ? ReadySize : NumberSize;
        HudStyle.DrawOutlined(this, new Vector2(x, baseline), _reloadText, size, _ready ? HudStyle.Accent : HudStyle.Text);
        if (!_ready) HudStyle.DrawOutlined(this, new Vector2(x + HudStyle.TextWidth(_reloadText, NumberSize) + 6f, baseline), "秒", UnitSize, HudStyle.Text, 4);
    }

    private void DrawChips(float left, float middle)
    {
        float x = left + Pad;
        DrawChip(x, middle, _apText, _ammo == AmmoType.AP, _apLeft);
        DrawChip(x + _chipWidth + ChipGap, middle, _heText, _ammo == AmmoType.HE, _heLeft);
    }

    private void DrawChip(float x, float middle, string text, bool loaded, int roundsLeft)
    {
        var rect = new Rect2(x, middle - ChipRow / 2 + 2, _chipWidth, ChipRow - 4);
        DrawStyleBox(loaded ? _chipLoaded : _chip, rect);
        Color color = roundsLeft == 0 ? Palette.HitBlocked.ToColor() : loaded ? HudStyle.Accent : HudStyle.Text;
        HudStyle.DrawOutlinedCentered(this, rect.Position.X + rect.Size.X / 2, HudStyle.Baseline(middle, ChipSize), text, ChipSize, color, 4);
    }

    /// <summary>The loaded round's chip: tinted and outlined in the accent colour.</summary>
    private static StyleBoxFlat LoadedChip()
    {
        StyleBoxFlat box = HudStyle.RoundedBox(new Color(HudStyle.Accent, 0.22f), ChipRadius, 6);
        box.BorderColor = HudStyle.Accent;
        box.SetBorderWidthAll(3);
        return box;
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

    /// <summary>A small cartoon heart, about 18 px across, centred on the origin.</summary>
    private static Vector2[] HeartShape()
    {
        var points = new Vector2[32];
        for (int i = 0; i < points.Length; i++)
        {
            float t = Mathf.Tau * i / points.Length;
            float x = 16f * MathF.Pow(MathF.Sin(t), 3);
            float y = 13f * MathF.Cos(t) - 5f * MathF.Cos(2 * t) - 2f * MathF.Cos(3 * t) - MathF.Cos(4 * t);
            points[i] = new Vector2(x, -y - 2f) * 0.55f;
        }
        return points;
    }

    private static Vector2[] ScaleAll(Vector2[] points, float factor)
    {
        var scaled = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++) scaled[i] = points[i] * factor;
        return scaled;
    }
}
