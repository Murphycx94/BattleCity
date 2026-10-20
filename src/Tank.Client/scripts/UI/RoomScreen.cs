using Godot;
using Tank.Client.Art;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Client.UI;

/// <summary>
/// The room: who is here (two columns seen from your seat, or one in a mode without teams), and on the right the controls
/// — mode and bots for the host, tank for everyone, swap sides, ready, start, leave. What shows and what is enabled comes
/// from <see cref="RoomModel"/>; this class only builds controls from it, and only when the model's signature changes.
/// </summary>
public partial class RoomScreen : Control
{
    private const int PanelPadding = 22;
    private const int ColumnMinWidth = 380;
    private const int SidePanelWidth = 420;

    private static readonly Color MineColor = Palette.AllyBody.ToColor();
    private static readonly Color OtherColor = Palette.EnemyBody.ToColor();
    private static readonly Color OwnColor = Palette.TankBody.ToColor();

    private readonly Label _title = UiKit.Label("", 40, UiKit.Accent);
    private readonly Label _rule = UiKit.Label("", 16, UiKit.Dim);
    private readonly PanelContainer _waitingBanner = UiKit.Panel(new Color(UiKit.Accent, 0.14f), 14, new Color(UiKit.Accent, 0.6f));
    private readonly Label _waitingText = UiKit.Label("", 20, UiKit.Accent);
    private readonly HBoxContainer _columns = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly OptionButton _mode = new();
    private readonly OptionButton _bots = new();
    private readonly GridContainer _tanks = new() { Columns = 2 };
    private readonly List<Button> _tankButtons = new();
    private readonly Button _switchTeam;
    private readonly Button _ready;
    private readonly Button _start;
    private readonly Label _hint = UiKit.Label("", 15, UiKit.Dim);
    private readonly Label _addressLine = UiKit.Label("", 14, UiKit.Dim);
    private BattleClient? _client;
    private RoomModel? _model;
    private string _signature = "";
    private RoomStateMessage? _builtFrom;   // the room state (and slot) the model was last built from: BattleClient replaces Room only when a new one arrives
    private byte _builtForSlot;
    private bool _updating;

    public event Action? LeaveRequested;

    public RoomScreen()
    {
        _switchTeam = UiKit.Button("换到对面", () => { if (_model is { } m) _client?.SetTeam(m.OtherTeam); });
        _ready = UiKit.Button("准备", () => { if (_model is { } m) _client?.SetReady(!m.MyReady); });
        _start = UiKit.Button("开始对战", () => _client?.StartMatch(), primary: true);
    }

    public override void _Ready()
    {
        UiKit.FillViewport(this);
        Theme = UiKit.Theme;
        var bg = new ColorRect { Color = UiKit.Background, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var margin = new MarginContainer();
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{edge}", 64);
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 14);
        margin.AddChild(col);
        col.AddChild(_title);
        col.AddChild(_rule);
        _waitingBanner.AddChild(_waitingText);
        _waitingBanner.Visible = false;
        col.AddChild(_waitingBanner);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 32);
        col.AddChild(body);
        _columns.AddThemeConstantOverride("separation", 20);
        _columns.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        body.AddChild(_columns);
        body.AddChild(BuildSide());
    }

    /// <summary>Starts showing <paramref name="client"/>'s room; <paramref name="addresses"/> is what friends can type to join the host.</summary>
    public void Bind(BattleClient client, string addresses)
    {
        _client = client;
        _signature = "";
        _model = null;
        _builtFrom = null;
        _addressLine.Text = addresses.Length == 0 ? "" : $"朋友可以在房间列表里找到你，或者输入 {addresses} 加入。";
    }

    /// <summary>Call every frame: the model is built only from a room state (or slot) not seen before, and the controls rebuilt only when it shows something new.</summary>
    public void Refresh()
    {
        if (_client is null) return;
        if (_client.Room is not { } room)
        {
            SetWaiting("正在进入房间…");
            return;
        }
        if (ReferenceEquals(room, _builtFrom) && _client.Slot == _builtForSlot) return;   // RoomModel.Build reads nothing else
        (_builtFrom, _builtForSlot) = (room, _client.Slot);
        RoomModel model = RoomModel.Build(room, _client.Slot);
        if (model.Signature == _signature) return;
        _signature = model.Signature;
        _model = model;
        Apply(model);
    }

    private void SetWaiting(string text)
    {
        _title.Text = text;
        _rule.Text = "";
    }

    private void Apply(RoomModel m)
    {
        _updating = true;
        _title.Text = m.Title;
        _rule.Text = m.RuleLine;
        _waitingBanner.Visible = m.WaitingText is not null;
        _waitingText.Text = m.WaitingText ?? "";

        _mode.Select(_mode.GetItemIndex((int)m.Mode));
        _mode.Disabled = !m.CanSetMode;
        _bots.Select(Math.Clamp(m.Bots, 0, _bots.ItemCount - 1));
        _bots.Disabled = !m.CanSetBots;
        for (int i = 0; i < _tankButtons.Count; i++) _tankButtons[i].SetPressedNoSignal(i == m.MyTankIndex);
        _switchTeam.Visible = m.HasTeams;
        _switchTeam.Disabled = !m.CanSwitchTeam;
        _ready.Text = m.MyReady ? "✓ 已准备（点击取消）" : "准备";
        _start.Visible = m.ShowStart;
        _start.Disabled = !m.CanStart;
        _hint.Visible = m.ShowStart;
        _hint.Text = m.StartHint;
        _hint.AddThemeColorOverride("font_color", m.CanStart ? UiKit.Good : UiKit.Dim);
        _addressLine.Visible = m.IsHost;
        _updating = false;

        foreach (Node child in _columns.GetChildren()) child.QueueFree();
        foreach (RoomColumnView column in m.Columns) _columns.AddChild(BuildColumn(column));
    }

    private Control BuildSide()
    {
        var panel = UiKit.Panel(pad: PanelPadding);
        panel.CustomMinimumSize = new Vector2(SidePanelWidth, 0);
        panel.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var side = new VBoxContainer();
        side.AddThemeConstantOverride("separation", 10);
        panel.AddChild(side);

        side.AddChild(UiKit.Label("模式（房主）", 14, UiKit.Dim));
        foreach (BattleMode mode in ModeRules.All) _mode.AddItem(ModeText.Name(mode), (int)mode);
        _mode.ItemSelected += i => { if (!_updating) _client?.SetMode((BattleMode)_mode.GetItemId((int)i)); };
        side.AddChild(_mode);

        side.AddChild(UiKit.Label("机器人（房主）", 14, UiKit.Dim));
        for (int n = 0; n <= BattleProtocol.MaxPlayers - 1; n++) _bots.AddItem(n == 0 ? "没有机器人" : $"{n} 个机器人", n);
        _bots.ItemSelected += i => { if (!_updating) _client?.SetBots(_bots.GetItemId((int)i)); };
        side.AddChild(_bots);

        side.AddChild(UiKit.Label("我的坦克", 14, UiKit.Dim));
        _tanks.AddThemeConstantOverride("h_separation", 8);
        _tanks.AddThemeConstantOverride("v_separation", 8);
        var group = new ButtonGroup();
        for (int i = 0; i < TankCatalog.All.Count; i++)
        {
            int index = i;
            Button b = UiKit.Button(ModeText.TankName(i), () => { if (!_updating) _client?.SetTank(TankCatalog.All[index].Id); });
            b.ToggleMode = true;
            b.ButtonGroup = group;
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _tankButtons.Add(b);
            _tanks.AddChild(b);
        }
        side.AddChild(_tanks);

        side.AddChild(UiKit.Gap(6));
        side.AddChild(_switchTeam);
        side.AddChild(_ready);
        side.AddChild(_start);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        side.AddChild(_hint);
        _addressLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        side.AddChild(_addressLine);
        side.AddChild(UiKit.Gap(14));
        side.AddChild(UiKit.Button("离开房间", () => LeaveRequested?.Invoke()));
        return panel;
    }

    private static Control BuildColumn(RoomColumnView column)
    {
        Color accent = column.Side switch { ColumnSide.Mine => MineColor, ColumnSide.Other => OtherColor, _ => UiKit.Accent };
        var panel = UiKit.Panel(pad: PanelPadding, border: new Color(accent, 0.7f));
        panel.CustomMinimumSize = new Vector2(ColumnMinWidth, 0);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        panel.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);
        col.AddChild(UiKit.Label(column.Title, 22, accent));
        foreach (RoomRowView row in column.Rows) col.AddChild(BuildRow(row, column.Side));
        return panel;
    }

    private static Control BuildRow(RoomRowView row, ColumnSide side)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 12);
        // The bar uses the colour the tank has in the match: orange for you, blue-grey for a teammate, red for an opponent.
        Color bar = row.IsMe ? OwnColor : side == ColumnSide.Mine ? MineColor : OtherColor;
        if (row.Ghost) bar = new Color(bar, 0.35f);
        line.AddChild(new ColorRect { Color = bar, CustomMinimumSize = new Vector2(10, 28) });

        Label name = UiKit.Label(row.Name, 20, row.Ghost ? UiKit.Dim : UiKit.TextColor);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        line.AddChild(name);
        if (row.Tags.Length > 0) line.AddChild(UiKit.Label(row.Tags, 13, UiKit.Accent));
        if (row.TankName.Length > 0) line.AddChild(UiKit.Label(row.TankName, 14, UiKit.Dim));
        Color stateColor = row.Tone == RowTone.Good ? UiKit.Good : UiKit.Dim;
        line.AddChild(UiKit.Label(row.State, 14, stateColor));
        return line;
    }
}
