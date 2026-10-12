using Godot;
using Tank.Client.View;
using Tank.Core.World;
using Tank.Net.Protocol;
using Tank.Net.Transport;

namespace Tank.Client.UI;

/// <summary>Main menu: player name, host a room, or join one found on the LAN (or by IP).</summary>
public partial class MenuScreen : Control
{
    private readonly LineEdit _name = new() { PlaceholderText = "Your name", MaxLength = ProtocolInfo.MaxPlayerNameBytes };
    private readonly OptionButton _mode = new();
    private readonly LineEdit _ip = new() { PlaceholderText = "192.168.1.20", CustomMinimumSize = new Vector2(220, 0) };
    private readonly ItemList _rooms = new() { CustomMinimumSize = new Vector2(0, 260), SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Label _status = UiKit.Label("", 16, Palette.UiDim);
    private readonly List<DiscoveredRoom> _roomList = new();

    public event Action<string, GameMode>? HostRequested;
    public event Action<string, string, int>? JoinRequested;

    public string PlayerName
    {
        get => string.IsNullOrWhiteSpace(_name.Text) ? "Player" : _name.Text.Trim();
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
            string state = r.Info.MatchRunning ? "IN GAME" : "LOBBY";
            _rooms.AddItem($"{r.Info.RoomName,-18} {r.Info.Mode,-11} {r.Info.Players}/{r.Info.MaxPlayers}  {state,-7} {r.PingMs,3}ms   {r.EndPoint.Address}");
        }
        if (selected >= 0 && selected < _rooms.ItemCount) _rooms.Select(selected);
    }

    private Control BuildLeft()
    {
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.9f };
        col.AddThemeConstantOverride("separation", 14);
        col.AddChild(UiKit.Label("TANK", 112, Palette.UiAccent));
        col.AddChild(UiKit.Label("3D  ·  LAN BATTLE", 30, Palette.UiText));
        col.AddChild(UiKit.Label("Defend the eagle. Break the bricks. Bring friends.", 16, Palette.UiDim));
        col.AddChild(UiKit.Gap(28));

        col.AddChild(UiKit.Label("CALLSIGN", 14, Palette.UiDim));
        col.AddChild(_name);
        col.AddChild(UiKit.Gap(10));

        col.AddChild(UiKit.Label("HOST A ROOM", 14, Palette.UiDim));
        _mode.AddItem("Co-op campaign", (int)GameMode.Coop);
        _mode.AddItem("Team base battle", (int)GameMode.BaseBattle);
        _mode.AddItem("Free-for-all", (int)GameMode.Deathmatch);
        col.AddChild(_mode);
        col.AddChild(UiKit.Button("HOST GAME", () => HostRequested?.Invoke(PlayerName, (GameMode)_mode.GetSelectedId()), primary: true));
        col.AddChild(UiKit.Gap(10));
        col.AddChild(_status);

        var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddChild(spacer);
        col.AddChild(UiKit.Label("WASD / arrows move · J / Space fire · C camera · F3 net graph · Tab scores", 13, Palette.UiDim));
        return col;
    }

    private Control BuildRight()
    {
        var panel = UiKit.Panel(pad: 22);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        panel.AddChild(col);

        col.AddChild(UiKit.Label("ROOMS ON THIS NETWORK", 14, Palette.UiDim));
        _rooms.ItemActivated += index => JoinSelected((int)index);
        col.AddChild(_rooms);
        col.AddChild(UiKit.Button("JOIN SELECTED", () => JoinSelected(_rooms.GetSelectedItems().FirstOrDefault(-1)), primary: true));
        col.AddChild(UiKit.Gap(8));

        col.AddChild(UiKit.Label("NOT LISTED? JOIN BY ADDRESS", 14, Palette.UiDim));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _ip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(_ip);
        row.AddChild(UiKit.Button("JOIN", JoinByAddress));
        col.AddChild(row);
        col.AddChild(UiKit.Label("Rooms are found by UDP broadcast. Guest Wi-Fi or AP isolation can hide them — use the address instead.", 13, Palette.UiDim));
        return panel;
    }

    private void JoinSelected(int index)
    {
        if (index < 0 || index >= _roomList.Count)
        {
            SetStatus("Pick a room first.", error: true);
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
            SetStatus("Type the host's IP address.", error: true);
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
            SetStatus($"'{text}' is not a valid address.", error: true);
            return;
        }
        JoinRequested?.Invoke(PlayerName, text, port);
    }
}
