using System.Net;
using Godot;
using Tank.Client.Online;
using Tank.Net.Protocol;

namespace Tank.Client.UI;

/// <summary>
/// Main menu: your name, host a room, take the test drive, or join a room found on the LAN (or typed in). It only raises
/// events; <c>Main</c> starts the session. The LAN list comes from <see cref="LanBrowser"/> through <see cref="ShowRooms"/>.
/// </summary>
public partial class MenuScreen : Control
{
    private const int PanelPadding = 22;

    private readonly LineEdit _name = new() { PlaceholderText = "你的名字" };   // held to the wire's 24 bytes by OnNameChanged, not a character count
    private readonly LineEdit _address = new() { PlaceholderText = "192.168.1.20", CustomMinimumSize = new Vector2(220, 0) };
    private readonly ItemList _rooms = new() { CustomMinimumSize = new Vector2(0, 260), SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Label _status = UiKit.Label("", 16, UiKit.Dim);
    private readonly Label _lanNote = UiKit.Label("", 14, UiKit.Dim);
    private readonly Label _version = UiKit.Label("", 13, UiKit.Dim);
    private readonly PanelContainer _updateBanner = UiKit.Panel(new Color(UiKit.Accent, 0.12f), 14, new Color(UiKit.Accent, 0.6f));
    private readonly Label _updateText = UiKit.Label("", 15, UiKit.TextColor);
    private readonly Button _updateButton = new() { CustomMinimumSize = new Vector2(0, 40) };
    private List<LanRoomRow> _roomRows = new();
    private IPEndPoint? _selected;
    private IReadOnlyList<LanRoomRow>? _shownRows;   // the list last passed in: LanBrowser replaces it only on a change
    private string _roomSignature = "";
    private Action? _updateAction;

    public event Action<string>? HostRequested;
    public event Action<string, string, int>? JoinRequested;
    public event Action? SandboxRequested;

    public string PlayerName
    {
        get => string.IsNullOrWhiteSpace(_name.Text) ? "玩家" : _name.Text.Trim();
        set => _name.Text = NameLimit.Clip(value);
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

        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 48);
        margin.AddChild(columns);
        columns.AddChild(BuildLeft());
        columns.AddChild(BuildRight());
        _rooms.ItemSelected += OnRoomSelected;
        _rooms.ItemActivated += index => JoinRoomAt((int)index);
        _name.TextChanged += OnNameChanged;
    }

    /// <summary>The line under the buttons: progress, or what went wrong.</summary>
    public void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", error ? UiKit.Danger : UiKit.Dim);
    }

    public void SetVersion(string version) => _version.Text = $"v{version}";

    /// <summary>Shows the update banner; a null <paramref name="text"/> hides it.</summary>
    public void SetUpdate(string? text, string? buttonText = null, Action? onPressed = null)
    {
        _updateBanner.Visible = text is not null;
        _updateText.Text = text ?? string.Empty;
        _updateAction = onPressed;
        _updateButton.Visible = buttonText is not null;
        _updateButton.Text = buttonText ?? string.Empty;
    }

    /// <summary>
    /// The rooms heard on the LAN, and why searching fails (null when it works). Called every frame: a list it has
    /// already shown costs nothing, and the item list is rebuilt only when what it shows changed.
    /// </summary>
    public void ShowRooms(IReadOnlyList<LanRoomRow> rows, string? error)
    {
        _lanNote.Text = error ?? (rows.Count == 0 ? "还没有找到房间。可以自己开一个，或者在下面输入地址。" : "双击房间可以直接加入。");
        _lanNote.AddThemeColorOverride("font_color", error is null ? UiKit.Dim : UiKit.Danger);
        if (ReferenceEquals(rows, _shownRows)) return;
        _shownRows = rows;
        string signature = string.Join('\n', rows.Select(r => $"{r.EndPoint}|{r.Text}|{r.Joinable}"));
        if (signature == _roomSignature) return;
        _roomSignature = signature;
        _roomRows = rows.ToList();
        _rooms.Clear();
        int keep = -1;
        for (int i = 0; i < _roomRows.Count; i++)
        {
            _rooms.AddItem(_roomRows[i].Text);
            if (!_roomRows[i].Joinable) _rooms.SetItemCustomFgColor(i, UiKit.Dim.Darkened(0.2f));
            if (Equals(_roomRows[i].EndPoint, _selected)) keep = i;
        }
        if (keep >= 0) _rooms.Select(keep);
        else _selected = null;
    }

    private Control BuildLeft()
    {
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.9f };
        col.AddThemeConstantOverride("separation", 14);
        col.AddChild(UiKit.Label("坦克", 112, UiKit.Accent));
        col.AddChild(UiKit.Label("3D  ·  局域网对战", 30, UiKit.TextColor));
        col.AddChild(UiKit.Label("分成两队，打穿掩体，击毁对手。叫上朋友，或者带几个机器人。", 16, UiKit.Dim));
        col.AddChild(_version);
        col.AddChild(BuildUpdateBanner());
        col.AddChild(UiKit.Gap(20));

        col.AddChild(UiKit.Label("呼号", 14, UiKit.Dim));
        col.AddChild(_name);
        col.AddChild(UiKit.Gap(10));
        col.AddChild(UiKit.Button("开房间", () => HostRequested?.Invoke(PlayerName), primary: true));
        col.AddChild(UiKit.Button("试驾场", () => SandboxRequested?.Invoke()));
        col.AddChild(UiKit.Gap(6));
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_status);

        col.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        col.AddChild(UiKit.Label("WASD 移动 · 鼠标瞄准 · 左键开火 · 1 / 2 换弹种 · Shift 狙击镜 · Tab 计分板 · F3 网络面板", 13, UiKit.Dim));
        return col;
    }

    private Control BuildUpdateBanner()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        _updateText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _updateText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _updateText.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_updateText);
        _updateButton.Pressed += () => _updateAction?.Invoke();
        row.AddChild(_updateButton);
        _updateBanner.AddChild(row);
        _updateBanner.Visible = false;
        return _updateBanner;
    }

    private Control BuildRight()
    {
        var panel = UiKit.Panel(pad: PanelPadding);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        panel.AddChild(col);

        col.AddChild(UiKit.Label("局域网内的房间", 14, UiKit.Dim));
        col.AddChild(_rooms);
        _lanNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_lanNote);
        col.AddChild(UiKit.Button("加入所选房间", () => JoinRoomAt(_rooms.GetSelectedItems().FirstOrDefault(-1)), primary: true));
        col.AddChild(UiKit.Gap(8));

        col.AddChild(UiKit.Label("找不到房间？输入地址加入", 14, UiKit.Dim));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _address.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(_address);
        row.AddChild(UiKit.Button("加入", JoinByAddress));
        col.AddChild(row);
        var note = UiKit.Label($"房间靠 UDP 广播被发现，只会搜到使用默认端口 {ProtocolInfo.DefaultPort} 的房间：房主改了端口时，请输入“地址:端口”加入。" +
                               "访客 Wi-Fi 或开启了 AP 隔离的网络也搜不到，同样请用地址加入。", 13, UiKit.Dim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(note);
        return panel;
    }

    /// <summary>Keeps the box within the wire's limit as the player types or pastes, so the room shows the name as typed.</summary>
    private void OnNameChanged(string text)
    {
        string clipped = NameLimit.Clip(text);
        if (clipped.Length == text.Length) return;
        _name.Text = clipped;   // setting Text raises no TextChanged
        _name.CaretColumn = clipped.Length;
    }

    private void OnRoomSelected(long index)
    {
        if (index < 0 || index >= _roomRows.Count) return;
        LanRoomRow row = _roomRows[(int)index];
        _selected = row.EndPoint;
        if (row.WhyNot is { } why) SetStatus(why, error: !row.Joinable);
        else SetStatus("");
    }

    private void JoinRoomAt(int index)
    {
        if (index < 0 || index >= _roomRows.Count)
        {
            SetStatus("请先选择一个房间。", error: true);
            return;
        }
        LanRoomRow row = _roomRows[index];
        if (!row.Joinable)
        {
            SetStatus(row.WhyNot ?? "这个房间现在不能加入。", error: true);
            return;
        }
        JoinRequested?.Invoke(PlayerName, row.EndPoint.Address.ToString(), row.EndPoint.Port);
    }

    private void JoinByAddress()
    {
        string text = _address.Text.Trim();
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
        if (port is < 1 or > 65535)
        {
            SetStatus($"端口 {port} 不对，应在 1 到 65535 之间。", error: true);
            return;
        }
        if (!IPAddress.TryParse(text, out _) && Uri.CheckHostName(text) == UriHostNameType.Unknown)
        {
            SetStatus($"“{text}”不是有效的地址。", error: true);
            return;
        }
        JoinRequested?.Invoke(PlayerName, text, port);
    }
}
