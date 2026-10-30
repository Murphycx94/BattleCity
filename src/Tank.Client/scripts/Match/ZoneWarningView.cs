using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;

namespace Tank.Client.Match;

/// <summary>
/// The warning while the own live tank is outside the safe circle (stage 6 design §3.3): the screen's sides and corners
/// tinted in the wall's blue (a full-screen <see cref="ColorRect"/> under the <c>zone_vignette</c> shader, the middle left
/// clear) and "在安全区外 −4%/秒" (<see cref="ZoneText.Outside"/>) centred in <see cref="HudBoxes.ZoneWarning"/>, above
/// the banner's place. The tint fades in over <see cref="FadeSeconds"/> when the tank goes out and fades back when it
/// comes in: every frame moves one shader uniform, nothing is laid out again. The words are rebuilt only when the
/// percentage changes, and step aside while the scoreboard is up (<see cref="ShowText"/>: their box lies in the board's
/// top rows); the tint stays. The first child of the match HUD, so the score plate and the kill feed lie over the tint.
/// </summary>
public partial class ZoneWarningView : HudPanel
{
    public const float FadeSeconds = 0.25f;

    private const int TextSize = 28, TextOutline = 4;
    private const string ShaderName = "zone_vignette";

    private static readonly StringName StrengthParam = "strength", AspectParam = "aspect";
    private static readonly Color TextColor = Palette.ZoneWall.ToColor();

    private ColorRect _tint = null!;
    private ShaderMaterial _material = null!;
    private float _strength, _target;
    private bool _outside;
    private bool _textShown = true;
    private int _percent;
    private string _text = string.Empty;

    public override void _Ready()
    {
        base._Ready();
        _material = Materials.FromShader(ShaderName).With("tint", TextColor);
        _tint = new ColorRect { Material = _material, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_tint);
        _tint.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Resized += UpdateAspect;
        UpdateAspect();
    }

    private void UpdateAspect() => _material.SetShaderParameter(AspectParam, Size.Y > 0 ? Size.X / Size.Y : 1f);

    /// <summary>Call once a frame, shown or not: whether the tank is outside now, and what that costs.</summary>
    public void Update(in MatchHudFrame frame)
    {
        bool outside = frame.Zone.On && frame.Zone.Outside;
        int percent = frame.Zone.Percent;
        _target = outside ? 1f : 0f;
        if (outside == _outside && (!outside || percent == _percent)) return;
        (_outside, _percent) = (outside, percent);
        if (outside) _text = ZoneText.Outside(percent);
        QueueRedraw();
    }

    /// <summary>Whether the words draw (the tint does regardless): off while the scoreboard is up, whose rows their box lies in.</summary>
    public void ShowText(bool shown)
    {
        if (shown == _textShown) return;
        _textShown = shown;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_strength == _target) return;
        _strength = Mathf.MoveToward(_strength, _target, (float)delta / FadeSeconds);
        _material.SetShaderParameter(StrengthParam, _strength);
        _tint.Visible = _strength > 0f;
    }

    public override void _Draw()
    {
        if (!_outside || !_textShown) return;
        HudRect box = Boxes.ZoneWarning;
        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + HudStyle.Baseline(box.Height / 2, TextSize), _text, TextSize, TextColor, TextOutline);
    }
}
