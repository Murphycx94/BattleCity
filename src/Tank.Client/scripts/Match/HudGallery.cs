using Godot;
using Tank.Client.Art;
using Tank.Client.Minimap;
using Tank.Client.UI;
using Tank.Client.World;
using Tank.Sim.Maps;

namespace Tank.Client.Match;

/// <summary>
/// <c>--hud-demo NAME</c>: one screen of the match HUD drawn from canned data (<see cref="HudDemoFrames"/>) over a flat
/// dark background, with no session, so a screenshot shows the layout and is the same every time. The clock the feed
/// reads is frozen at 0, so its lines never expire. A screen with a map (<see cref="HudDemo.Minimap"/>) builds that
/// catalog map's battlefield and bakes its ground in <see cref="_Ready"/> (a long first frame), then shows the canned icons
/// over it, as the corner map or the full one.
/// </summary>
public partial class HudGallery : Node
{
    private const int BackgroundLayer = 0;

    private readonly HudDemo _demo;
    private MatchHud _hud = null!;
    private MinimapLayer? _minimap;
    private Battlefield? _field;

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
        foreach (FeedLine k in _demo.Kills)
        {
            if (k.Zone) _hud.OnZoneKill(k.Victim, k.VictimSide, now: 0);
            else _hud.OnKill(k.Killer, k.KillerSide, k.Victim, k.VictimSide, k.Friendly, now: 0);
        }
        _hud.SetScoreboardOpen(_demo.ScoreboardOpen);
        if (_demo.Minimap is { } m) AddMinimap(m);
        GetWindow().Title = $"坦克 3D · HUD · {_demo.Name}";
    }

    /// <summary>The map's ground as the scenery would bake it: the field's own bare-ground mask (none: all grass), baked here and now.</summary>
    private void AddMinimap(MinimapDemo m)
    {
        Battlefield field = MapCatalog.Get(m.MapId).NewBattlefield();
        DirtMask? dirt = DirtMask.FromField(field);
        Func<float, float, float> notGrassAt = dirt is null ? (_, _) => 0f : dirt.At;
        MinimapGround ground = MinimapGround.Bake(field, notGrassAt);
        _minimap = new MinimapLayer();
        AddChild(_minimap);
        _minimap.BuildBaked(ground, field, MapText.Name(m.MapId));
        if (m.FullOpen) _minimap.ToggleFull();
        _field = field;
    }

    public override void _Process(double delta)
    {
        _hud.Refresh(_demo.Frame, _demo.NetPanelOpen, now: 0);
        if (_minimap is { } minimap && _field is { } field && _demo.Minimap is { } m) minimap.Show(m.Frame, field);
    }
}
