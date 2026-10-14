using Godot;
using Tank.Client.Art;

namespace Tank.Client.Overlay;

/// <summary>Dark rounded vignette around the screen edge, shown while looking through the sniper sight.</summary>
public partial class SniperVignette : TextureRect
{
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        StretchMode = StretchModeEnum.Scale;
        Color dark = Palette.UiOutline.ToColor();
        Texture = new GradientTexture2D
        {
            Width = 256,
            Height = 256,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 0.5f),
            Gradient = new Gradient
            {
                Offsets = new[] { 0f, 0.62f, 0.9f, 1f },
                Colors = new[] { new Color(dark, 0f), new Color(dark, 0f), new Color(dark, 0.75f), new Color(dark, 0.92f) },
            },
        };
        Visible = false;
    }
}
