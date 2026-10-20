using Godot;
using Tank.Client.Art;

namespace Tank.Client.Match;

/// <summary>
/// <c>--hud-demo NAME</c>: one screen of the match HUD drawn from canned data (<see cref="HudDemoFrames"/>) over a flat
/// dark background, with no session, so a screenshot shows the layout and is the same every time. The clock the feed
/// reads is frozen at 0, so its lines never expire.
/// </summary>
public partial class HudGallery : Node
{
    private const int BackgroundLayer = 0;

    private readonly HudDemo _demo;
    private MatchHud _hud = null!;

    public HudGallery(HudDemo demo) => _demo = demo;

    public override void _Ready()
    {
        var back = new CanvasLayer { Layer = BackgroundLayer };
        var fill = new ColorRect { Color = Palette.UiBackground.ToColor(), MouseFilter = Control.MouseFilterEnum.Ignore };
        back.AddChild(fill);
        fill.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(back);
        _hud = new MatchHud();
        AddChild(_hud);
        foreach (FeedLine k in _demo.Kills) _hud.OnKill(k.Killer, k.KillerSide, k.Victim, k.VictimSide, k.Friendly, now: 0);
        _hud.SetScoreboardOpen(_demo.ScoreboardOpen);
        GetWindow().Title = $"坦克 3D · HUD · {_demo.Name}";
    }

    public override void _Process(double delta) => _hud.Refresh(_demo.Frame, _demo.NetPanelOpen, now: 0);
}
