using Godot;
using Tank.Client.Art;

namespace Tank.Client.Overlay;

/// <summary>
/// The cartoon HUD theme: the bundled rounded CJK font (ZCOOL KuaiLe, OFL), palette colours, outlined text, rounded
/// boxes and the health-bar colour rule. Text is always drawn with a dark outline so it reads over bright sky and grass alike.
/// </summary>
public static class HudStyle
{
    public const string FontPath = "res://assets/fonts/ZCOOLKuaiLe-Regular.ttf";
    public const int Outline = 6;

    /// <summary>At or below this share of its health a health bar turns red.</summary>
    public const float LowHealth = 0.3f;

    private const float Embolden = 0.7f;        // ZCOOL KuaiLe is a regular weight; thicken it into a chunky cartoon face
    private const float BaselineDrop = 0.36f;   // the baseline sits this many ems below the middle of the glyphs

    private static readonly Lazy<Font> LoadedFont = new(() => new FontVariation { BaseFont = GD.Load<FontFile>(FontPath), VariationEmbolden = Embolden });

    public static Font Font => LoadedFont.Value;
    public static Color Text => Palette.UiText.ToColor();
    public static Color OutlineColor => Palette.UiOutline.ToColor();
    public static Color Accent => Palette.UiAccent.ToColor();
    public static Color Panel => Palette.UiPanel.ToColor(Palette.UiPanelAlpha);

    /// <summary>The baseline that centres text of <paramref name="size"/> vertically on <paramref name="middle"/>.</summary>
    public static float Baseline(float middle, int size) => middle + size * BaselineDrop;

    /// <summary>
    /// Text with a dark outline; <paramref name="baseline"/> is the left end of the text baseline. The outline fades
    /// with the fill: it takes <paramref name="color"/>'s alpha.
    /// </summary>
    public static void DrawOutlined(CanvasItem canvas, Vector2 baseline, string text, int size, Color color, int outline = Outline)
    {
        canvas.DrawStringOutline(Font, baseline, text, HorizontalAlignment.Left, -1, size, outline, new Color(OutlineColor, color.A));
        canvas.DrawString(Font, baseline, text, HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>Same, centred horizontally on <paramref name="centerX"/>.</summary>
    public static void DrawOutlinedCentered(CanvasItem canvas, float centerX, float baselineY, string text, int size, Color color, int outline = Outline)
    {
        DrawOutlined(canvas, new Vector2(centerX - TextWidth(text, size) / 2, baselineY), text, size, color, outline);
    }

    public static float TextWidth(string text, int size) => Font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;

    /// <summary>A ring with a dark outline: the outline is drawn first, wider, then the coloured ring on top.</summary>
    public static void DrawOutlinedArc(CanvasItem canvas, Vector2 center, float radius, float from, float to, Color color, float width, float outline = 2f)
    {
        canvas.DrawArc(center, radius, from, to, 48, OutlineColor, width + 2 * outline, true);
        canvas.DrawArc(center, radius, from, to, 48, color, width, true);
    }

    /// <summary>A filled, anti-aliased box with rounded corners.</summary>
    public static StyleBoxFlat RoundedBox(Color color, int radius, int cornerDetail)
    {
        var box = new StyleBoxFlat { BgColor = color, CornerDetail = cornerDetail, AntiAliasing = true };
        box.SetCornerRadiusAll(radius);
        return box;
    }

    /// <summary>A health bar's fill, built once in both colours: green, or red at <see cref="LowHealth"/> and below.</summary>
    public sealed class HealthFill
    {
        private readonly StyleBoxFlat _healthy;
        private readonly StyleBoxFlat _low;

        public HealthFill(int radius, int cornerDetail)
        {
            _healthy = RoundedBox(Palette.Health.ToColor(), radius, cornerDetail);
            _low = RoundedBox(Palette.HitPierced.ToColor(), radius, cornerDetail);
        }

        /// <summary>The fill for a bar with <paramref name="share"/> (0 … 1) of its health left.</summary>
        public StyleBoxFlat For(float share) => share <= LowHealth ? _low : _healthy;
    }
}
