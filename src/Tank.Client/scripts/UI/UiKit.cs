using Godot;
using Tank.Client.View;

namespace Tank.Client.UI;

/// <summary>Shared look for all screens: monospace arcade type, amber accent, hard-edged panels.</summary>
public static class UiKit
{
    private static Theme? _theme;

    public static Theme Theme => _theme ??= BuildTheme();

    public static Label Label(string text, int size = 18, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Palette.UiText);
        return l;
    }

    public static PanelContainer Panel(Color? bg = null, int pad = 18, Color? border = null)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Box(bg ?? Palette.UiPanel, pad, border ?? new Color(1, 1, 1, 0.08f)));
        return p;
    }

    public static Button Button(string text, Action onPressed, bool primary = false)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 44), FocusMode = Control.FocusModeEnum.All };
        if (primary)
        {
            b.AddThemeStyleboxOverride("normal", Box(Palette.UiAccent, 10));
            b.AddThemeStyleboxOverride("hover", Box(Palette.UiAccent.Lightened(0.15f), 10));
            b.AddThemeStyleboxOverride("pressed", Box(Palette.UiAccent.Darkened(0.2f), 10));
            b.AddThemeStyleboxOverride("focus", Box(new Color(0, 0, 0, 0), 10, Colors.White));
            b.AddThemeColorOverride("font_color", new Color(0.08f, 0.06f, 0.02f));
            b.AddThemeColorOverride("font_hover_color", new Color(0.08f, 0.06f, 0.02f));
            b.AddThemeColorOverride("font_pressed_color", new Color(0.08f, 0.06f, 0.02f));
        }
        b.Pressed += onPressed;
        return b;
    }

    public static StyleBoxFlat Box(Color bg, int pad, Color? border = null)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg,
            ContentMarginLeft = pad,
            ContentMarginRight = pad,
            ContentMarginTop = pad * 0.6f,
            ContentMarginBottom = pad * 0.6f,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
        };
        if (border is { } c)
        {
            sb.BorderColor = c;
            sb.BorderWidthBottom = sb.BorderWidthTop = sb.BorderWidthLeft = sb.BorderWidthRight = 2;
        }
        return sb;
    }

    /// <summary>
    /// Makes a screen-level Control cover the whole window. Screens are children of a plain Node, which gives
    /// anchors nothing to stretch against, so size them to the viewport explicitly and follow resizes.
    /// </summary>
    public static void FillViewport(Control screen)
    {
        void Fit()
        {
            screen.Position = Vector2.Zero;
            screen.Size = screen.GetViewportRect().Size;
        }
        Fit();
        screen.GetViewport().SizeChanged += Fit;
    }

    public static Control Gap(int px) => new Control { CustomMinimumSize = new Vector2(px, px) };

    private static Theme BuildTheme()
    {
        var font = new SystemFont
        {
            FontNames = new[] { "JetBrains Mono", "SF Mono", "Menlo", "Consolas", "DejaVu Sans Mono", "monospace" },
            FontWeight = 600,
        };
        var theme = new Theme { DefaultFont = font, DefaultFontSize = 18 };
        var normal = Box(new Color(0.13f, 0.14f, 0.19f), 10, new Color(1, 1, 1, 0.1f));
        var hover = Box(new Color(0.18f, 0.19f, 0.25f), 10, Palette.UiAccent);
        theme.SetStylebox("normal", "Button", normal);
        theme.SetStylebox("hover", "Button", hover);
        theme.SetStylebox("pressed", "Button", Box(new Color(0.09f, 0.1f, 0.13f), 10, Palette.UiAccent));
        theme.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), 10, Palette.UiAccent));
        theme.SetStylebox("disabled", "Button", Box(new Color(0.1f, 0.1f, 0.12f), 10));
        theme.SetColor("font_color", "Button", Palette.UiText);
        theme.SetColor("font_hover_color", "Button", Palette.UiAccent);
        theme.SetStylebox("normal", "LineEdit", Box(new Color(0.05f, 0.06f, 0.08f), 10, new Color(1, 1, 1, 0.12f)));
        theme.SetStylebox("focus", "LineEdit", Box(new Color(0, 0, 0, 0), 10, Palette.UiAccent));
        theme.SetStylebox("panel", "ItemList", Box(new Color(0.05f, 0.06f, 0.08f), 8, new Color(1, 1, 1, 0.12f)));
        theme.SetColor("font_selected_color", "ItemList", Palette.UiAccent);
        theme.SetStylebox("normal", "OptionButton", normal);
        theme.SetStylebox("hover", "OptionButton", hover);
        return theme;
    }
}
