using Godot;
using Tank.Client.View;
using Tank.Core.World;
using Tank.Net.Client;
using Tank.Net.Protocol;

namespace Tank.Client.UI;

/// <summary>Room lobby: who is here, mode/map (host chooses), ready up, start.</summary>
public partial class LobbyScreen : Control
{
    private readonly Label _title = UiKit.Label("", 40, Palette.UiAccent);
    private readonly Label _subtitle = UiKit.Label("", 16, Palette.UiDim);
    private readonly VBoxContainer _players = new();
    private readonly OptionButton _mode = new();
    private readonly OptionButton _map = new();
    private readonly Button _ready;
    private readonly Button _start;
    private readonly HBoxContainer _teamRow = new();
    private readonly Label _hint = UiKit.Label("", 15, Palette.UiDim);
    private GameClient? _client;
    private string _lastSignature = "";
    private bool _updating;

    public event Action? LeaveRequested;

    public LobbyScreen()
    {
        _ready = UiKit.Button("准备", () => _client?.SetReady(!IsReady()));
        _start = UiKit.Button("开始对战", () => _client?.StartMatch(), primary: true);
    }

    public string LocalAddresses { get; set; } = "";

    public override void _Ready()
    {
        UiKit.FillViewport(this);
        Theme = UiKit.Theme;
        // Translucent so the 3D preview of the selected map shows through.
        var shade = new ColorRect { Color = new Color(Palette.Background, 0.45f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        foreach (var edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{edge}", 64);
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 16);
        margin.AddChild(col);
        col.AddChild(_title);
        col.AddChild(_subtitle);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 32);
        col.AddChild(body);

        var playersPanel = UiKit.Panel(pad: 22);
        playersPanel.CustomMinimumSize = new Vector2(520, 0);
        playersPanel.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var pcol = new VBoxContainer();
        pcol.AddThemeConstantOverride("separation", 10);
        pcol.AddChild(UiKit.Label("玩家", 14, Palette.UiDim));
        _players.AddThemeConstantOverride("separation", 8);
        pcol.AddChild(_players);
        playersPanel.AddChild(pcol);
        body.AddChild(playersPanel);
        body.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore }); // let the map show

        var sidePanel = UiKit.Panel(pad: 22);
        sidePanel.CustomMinimumSize = new Vector2(420, 0);
        sidePanel.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var side = new VBoxContainer();
        sidePanel.AddChild(side);
        side.AddThemeConstantOverride("separation", 12);
        side.AddChild(UiKit.Label("模式", 14, Palette.UiDim));
        foreach (var m in Enum.GetValues<GameMode>()) _mode.AddItem(ModeName(m), (int)m);
        _mode.ItemSelected += i => { if (!_updating) _client?.SetMode((GameMode)_mode.GetItemId((int)i)); };
        side.AddChild(_mode);
        side.AddChild(UiKit.Label("地图", 14, Palette.UiDim));
        foreach (var name in MapLibrary.Names) _map.AddItem(MapName(name));
        _map.ItemSelected += i => { if (!_updating) _client?.SetMap(MapLibrary.Names.ElementAt((int)i)); };
        side.AddChild(_map);
        _teamRow.AddThemeConstantOverride("separation", 10);
        _teamRow.AddChild(UiKit.Button("加入金队", () => _client?.SetTeam(0)));
        _teamRow.AddChild(UiKit.Button("加入蓝队", () => _client?.SetTeam(1)));
        side.AddChild(_teamRow);
        side.AddChild(UiKit.Gap(12));
        side.AddChild(_ready);
        side.AddChild(_start);
        side.AddChild(_hint);
        side.AddChild(UiKit.Gap(24));
        side.AddChild(UiKit.Button("离开房间", () => LeaveRequested?.Invoke()));
        body.AddChild(sidePanel);
    }

    public void Bind(GameClient client) => _client = client;

    public void Refresh()
    {
        var lobby = _client?.Lobby;
        if (_client is null) return;
        if (lobby is null)
        {
            _title.Text = "正在连接…";
            _subtitle.Text = "等待房主响应。";
            return;
        }
        string signature = $"{lobby.Mode}|{lobby.MapName}|{lobby.HostSlot}|{string.Join(";", lobby.Players)}|{_client.Slot}";
        if (signature == _lastSignature) return;
        _lastSignature = signature;

        _updating = true;
        _title.Text = $"{ModeName(lobby.Mode)}  ·  {MapName(lobby.MapName)}";
        _subtitle.Text = _client.IsHost
            ? $"你是房主。朋友可以在房间列表中找到你，或通过 {LocalAddresses} 加入。"
            : "等待房主开始对战。";
        _mode.Select(_mode.GetItemIndex((int)lobby.Mode));
        int mapIndex = MapLibrary.Names.ToList().IndexOf(lobby.MapName);
        if (mapIndex >= 0) _map.Select(mapIndex);
        _mode.Disabled = _map.Disabled = !_client.IsHost;
        _start.Visible = _client.IsHost;
        _teamRow.Visible = lobby.Mode == GameMode.BaseBattle;
        _ready.Text = IsReady() ? "✓ 已准备" : "准备";
        int ready = lobby.Players.Count(p => p.Ready);
        _hint.Text = _client.IsHost ? $"{ready}/{lobby.Players.Count} 人已准备 — 随时可以开始。" : "";
        _updating = false;

        foreach (var child in _players.GetChildren()) child.QueueFree();
        foreach (var p in lobby.Players) _players.AddChild(PlayerRow(p, lobby));
    }

    private Control PlayerRow(LobbyPlayer p, LobbyStateMessage lobby)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(new ColorRect { Color = Palette.ForSlot(p.Slot), CustomMinimumSize = new Vector2(14, 28) });
        string tags = (p.Slot == lobby.HostSlot ? " 房主" : "") + (p.Slot == _client?.Slot ? " 你" : "");
        var name = UiKit.Label($"{p.Name}", 22, p.Connected ? Palette.UiText : Palette.UiDim);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(name);
        row.AddChild(UiKit.Label(tags.Trim(), 13, Palette.UiAccent));
        if (lobby.Mode == GameMode.BaseBattle)
            row.AddChild(UiKit.Label(p.Team == 0 ? "金队" : "蓝队", 14, Palette.ForTeam(p.Team)));
        row.AddChild(UiKit.Label(!p.Connected ? "重连中" : p.Ready ? "已准备" : "—", 14, p.Ready ? Palette.UiGood : Palette.UiDim));
        return row;
    }

    private bool IsReady() => _client?.Lobby?.Players.FirstOrDefault(p => p.Slot == _client.Slot)?.Ready ?? false;

    public static string ModeName(GameMode mode) => mode switch
    {
        GameMode.Coop => "合作闯关",
        GameMode.BaseBattle => "团队守家",
        _ => "自由混战",
    };

    /// <summary>Display name for a map; the id itself stays on the wire.</summary>
    public static string MapName(string id) => id switch
    {
        "coop-01" => "合作关卡 01",
        "versus-01" => "对抗战场 01",
        "arena-01" => "混战竞技场 01",
        _ => id,
    };
}
