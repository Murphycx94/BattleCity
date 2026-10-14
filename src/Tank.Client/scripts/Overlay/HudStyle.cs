using Godot;
using Tank.Client.Art;

namespace Tank.Client.Overlay;

/// <summary>
/// The cartoon HUD theme: the bundled rounded CJK font (ZCOOL KuaiLe, OFL), palette colours, and outlined text.
/// Text is always drawn with a dark outline so it reads over bright sky and grass alike.
/// </summary>
public static class HudStyle
{
    public const string FontPath = "res://assets/fonts/ZCOOLKuaiLe-Regular.ttf";
    public const int Outline = 6;

    private const float Embolden = 0.7f;   // ZCOOL KuaiLe is a regular weight; thicken it into a chunky cartoon face

    private static readonly Lazy<Font> LoadedFont = new(() => new FontVariation { BaseFont = GD.Load<FontFile>(FontPath), VariationEmbolden = Embolden });

    public static Font Font => LoadedFont.Value;
    public static Color Text => Palette.UiText.ToColor();
    public static Color OutlineColor => Palette.UiOutline.ToColor();
    public static Color Accent => Palette.UiAccent.ToColor();
    public static Color Panel => Palette.UiPanel.ToColor(Palette.UiPanelAlpha);

    /// <summary>Text with a dark outline; <paramref name="baseline"/> is the left end of the text baseline.</summary>
    public static void DrawOutlined(CanvasItem canvas, Vector2 baseline, string text, int size, Color color, int outline = Outline)
    {
        canvas.DrawStringOutline(Font, baseline, text, HorizontalAlignment.Left, -1, size, outline, OutlineColor);
        canvas.DrawString(Font, baseline, text, HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>Same, centred horizontally on <paramref name="centerX"/>.</summary>
    public static void DrawOutlinedCentered(CanvasItem canvas, float centerX, float baselineY, string text, int size, Color color, int outline = Outline)
    {
        float width = Font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawOutlined(canvas, new Vector2(centerX - width / 2, baselineY), text, size, color, outline);
    }

    public static float TextWidth(string text, int size) => Font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;

    /// <summary>A ring with a dark outline: the outline is drawn first, wider, then the coloured ring on top.</summary>
    public static void DrawOutlinedArc(CanvasItem canvas, Vector2 center, float radius, float from, float to, Color color, float width, float outline = 2f)
    {
        canvas.DrawArc(center, radius, from, to, 48, OutlineColor, width + 2 * outline, true);
        canvas.DrawArc(center, radius, from, to, 48, color, width, true);
    }
}
