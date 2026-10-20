using Godot;
using Tank.Client.UI;

namespace Tank.Client.Match;

/// <summary>
/// The pause layer (blueprint §4.2, R14): Esc lets the mouse go and brings this up over the match — a real, clickable
/// control, a 440×280 panel in the middle over a dimmed screen. 继续 asks for the mouse back, 离开房间 hands the leave
/// upward (the scene must not drop the session itself), and the host alone gets 结束对局回到房间 (<c>ReturnToRoom</c>).
/// Pausing is local: the match runs on underneath, and the results screen closes the layer so it never covers the verdict
/// or eats the host's Enter.
/// </summary>
public partial class PauseOverlay : CanvasLayer
{
    private const int OverlayLayer = 3;   // over the match HUD (2)
    private const float PanelWidth = 440f, PanelHeight = 280f;
    private const float DimAlpha = 0.45f;
    private const int TitleSize = 28, Spacing = 10;

    private readonly bool _isHost;
    private Button _resume = null!;

    /// <summary>继续: the owner hides the layer and takes the mouse back.</summary>
    public event Action? ResumeRequested;

    /// <summary>结束对局回到房间 (host only): the owner should send <c>ReturnToRoom</c>.</summary>
    public event Action? EndMatchRequested;

    /// <summary>离开房间: the owner should leave the whole session.</summary>
    public event Action? LeaveRequested;

    public PauseOverlay(bool isHost)
    {
        _isHost = isHost;
        Layer = OverlayLayer;
    }

    public override void _Ready()
    {
        // The dim swallows clicks beside the panel (MouseFilter Stop), so a stray click can't recapture the mouse under the layer.
        var dim = new ColorRect { Color = new Color(0, 0, 0, DimAlpha) };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer { Theme = UiKit.Theme };
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        PanelContainer panel = UiKit.Panel();
        panel.CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);
        center.AddChild(panel);
        var rows = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        rows.AddThemeConstantOverride("separation", Spacing);
        panel.AddChild(rows);
        rows.AddChild(UiKit.Label("已暂停", TitleSize, align: HorizontalAlignment.Center));
        rows.AddChild(UiKit.Gap(Spacing));
        _resume = UiKit.Button("继续", () => ResumeRequested?.Invoke(), primary: true);
        rows.AddChild(_resume);
        if (_isHost) rows.AddChild(UiKit.Button("结束对局回到房间", () => EndMatchRequested?.Invoke()));
        rows.AddChild(UiKit.Button("离开房间", () => LeaveRequested?.Invoke()));
    }

    /// <summary>Shows the layer with the keyboard on 继续 (Enter resumes).</summary>
    public void Open()
    {
        Visible = true;
        _resume.GrabFocus();
    }

    public void Close() => Visible = false;
}
