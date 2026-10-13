using Godot;
using Tank.Client.View;
using Tank.Core.World;
using Tank.Net.Protocol;
using Tank.Net.Transport;

namespace Tank.Client.UI;

/// <summary>Main menu: player name, host a room, or join one found on the LAN (or by IP).</summary>
public partial class MenuScreen : Control
{
    private readonly LineEdit _name = new() { PlaceholderText = "你的名字", MaxLength = ProtocolInfo.MaxPlayerNameBytes };
    private readonly OptionButton _mode = new();
    private readonly LineEdit _ip = new() { PlaceholderText = "192.168.1.20", CustomMinimumSize = new Vector2(220, 0) };
    private readonly ItemList _rooms = new() { CustomMinimumSize = new Vector2(0, 260), SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Label _status = UiKit.Label("", 16, Palette.UiDim);
    private readonly List<DiscoveredRoom> _roomList = new();

    public event Action<string, GameMode>? HostRequested;
    public event Action<string, string, int>? JoinRequested;

    public string PlayerName
    {
        get => string.IsNullOrWhiteSpace(_name.Text) ? "玩家" : _name.Text.Trim();
        set => _name.Text = value;
    }

    public override void _Ready()
    {
        UiKit.FillViewport(this);
        Theme = UiKit.Theme;
        var bg = new ColorRect { Color = Palette.Background, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var margin = new MarginContainer();
        foreach (var edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{edge}", 64);
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 48);
        margin.AddChild(columns);

        columns.AddChild(BuildLeft());
        columns.AddChild(BuildRight());
    }

    public void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", error ? Palette.UiDanger : Palette.UiDim);
    }

    public void ShowRooms(IReadOnlyCollection<DiscoveredRoom> rooms)
    {
        var ordered = rooms.OrderBy(r => r.Info.RoomName).ToList();
        if (ordered.Select(r => r.EndPoint.ToString() + r.Info.Players + r.Info.MatchRunning + r.PingMs / 5)
            .SequenceEqual(_roomList.Select(r => r.EndPoint.ToString() + r.Info.Players + r.Info.MatchRunning + r.PingMs / 5))) return;
        int selected = _rooms.GetSelectedItems().FirstOrDefault(-1);
        _roomList.Clear();
        _roomList.AddRange(ordered);
        _rooms.Clear();
        foreach (var r in _roomList)
        {
            string state = r.Info.MatchRunning ? "对战中" : "等待中";
            _rooms.AddItem($"{UiKit.Pad(r.Info.RoomName, 18)} {UiKit.Pad(LobbyScreen.ModeName(r.Info.Mode), 12)} {r.Info.Players}/{r.Info.MaxPlayers}  {UiKit.Pad(state, 7)} {r.PingMs,3}ms   {r.EndPoint.Address}");
        }
        if (selected >= 0 && selected < _rooms.ItemCount) _rooms.Select(selected);
    }

    private Control BuildLeft()
    {
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.9f };
        col.AddThemeConstantOverride("separation", 14);
        col.AddChild(UiKit.Label("坦克", 112, Palette.UiAccent));
        col.AddChild(UiKit.Label("3D  ·  局域网对战", 30, Palette.UiText));
        col.AddChild(UiKit.Label("守住老鹰，打穿砖墙，叫上朋友。", 16, Palette.UiDim));
        col.AddChild(UiKit.Gap(28));

        col.AddChild(UiKit.Label("呼号", 14, Palette.UiDim));
        col.AddChild(_name);
        col.AddChild(UiKit.Gap(10));

        col.AddChild(UiKit.Label("创建房间", 14, Palette.UiDim));
        foreach (var m in new[] { GameMode.Coop, GameMode.BaseBattle, GameMode.Deathmatch }) _mode.AddItem(LobbyScreen.ModeName(m), (int)m);
        col.AddChild(_mode);
        col.AddChild(UiKit.Button("开设房间", () => HostRequested?.Invoke(PlayerName, (GameMode)_mode.GetSelectedId()), primary: true));
        col.AddChild(UiKit.Gap(10));
        col.AddChild(_status);

        var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddChild(spacer);
        col.AddChild(UiKit.Label("WASD / 方向键 移动 · J / 空格 开火 · C 切换镜头 · F3 网络面板 · Tab 计分板 · Esc 离开", 13, Palette.UiDim));
        return col;
    }

    private Control BuildRight()
    {
        var panel = UiKit.Panel(pad: 22);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        panel.AddChild(col);

        col.AddChild(UiKit.Label("局域网内的房间", 14, Palette.UiDim));
        _rooms.ItemActivated += index => JoinSelected((int)index);
        col.AddChild(_rooms);
        col.AddChild(UiKit.Button("加入所选房间", () => JoinSelected(_rooms.GetSelectedItems().FirstOrDefault(-1)), primary: true));
        col.AddChild(UiKit.Gap(8));

        col.AddChild(UiKit.Label("找不到房间？输入地址加入", 14, Palette.UiDim));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _ip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(_ip);
        row.AddChild(UiKit.Button("加入", JoinByAddress));
        col.AddChild(row);
        col.AddChild(UiKit.Label("房间通过 UDP 广播发现。访客 Wi-Fi 或开启了 AP 隔离的网络可能搜不到，请改用地址加入。", 13, Palette.UiDim));
        return panel;
    }

    private void JoinSelected(int index)
    {
        if (index < 0 || index >= _roomList.Count)
        {
            SetStatus("请先选择一个房间。", error: true);
            return;
        }
        var room = _roomList[index];
        JoinRequested?.Invoke(PlayerName, room.EndPoint.Address.ToString(), room.EndPoint.Port);
    }

    private void JoinByAddress()
    {
        string text = _ip.Text.Trim();
        if (text.Length == 0)
        {
            SetStatus("请输入房主的 IP 地址。", error: true);
            return;
        }
        int port = ProtocolInfo.DefaultPort;
        int colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out int p))
        {
            port = p;
            text = text[..colon];
        }
        if (!System.Net.IPAddress.TryParse(text, out _) && Uri.CheckHostName(text) == UriHostNameType.Unknown)
        {
            SetStatus($"“{text}”不是有效的地址。", error: true);
            return;
        }
        JoinRequested?.Invoke(PlayerName, text, port);
    }
}
