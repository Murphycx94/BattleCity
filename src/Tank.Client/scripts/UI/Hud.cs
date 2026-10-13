using Godot;
using Tank.Client.View;
using Tank.Core;
using Tank.Core.World;
using Tank.Net.Client;
using Tank.Net.Protocol;

namespace Tank.Client.UI;

/// <summary>In-match overlay: status bar, scoreboard, end-of-match banner and the net_graph panel.</summary>
public partial class Hud : CanvasLayer
{
    private readonly Label _left = UiKit.Label("", 20);
    private readonly Label _center = UiKit.Label("", 22, Palette.UiAccent, HorizontalAlignment.Center);
    private readonly Label _right = UiKit.Label("", 16, Palette.UiText, HorizontalAlignment.Right);
    private readonly Label _banner = UiKit.Label("", 72, Palette.UiAccent, HorizontalAlignment.Center);
    private readonly Label _bannerSub = UiKit.Label("", 20, Palette.UiText, HorizontalAlignment.Center);
    private readonly Label _netGraph = UiKit.Label("", 14, Palette.UiGood);
    private readonly PanelContainer _netPanel = UiKit.Panel(new Color(0, 0, 0, 0.6f), 12);
    private readonly VBoxContainer _bannerBox = new();
    private readonly List<double> _frameTimes = new();

    public bool NetGraphVisible
    {
        get => _netPanel.Visible;
        set => _netPanel.Visible = value;
    }

    public string HostLine { get; set; } = "";

    public override void _Ready()
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiKit.Theme };
        AddChild(root);
        UiKit.FillViewport(root);

        var bar = UiKit.Panel(new Color(0.03f, 0.035f, 0.05f, 0.72f), 16);
        bar.AnchorRight = 1;
        var row = new HBoxContainer();
        _left.SizeFlagsHorizontal = _center.SizeFlagsHorizontal = _right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(_left);
        row.AddChild(_center);
        row.AddChild(_right);
        bar.AddChild(row);
        root.AddChild(bar);

        _bannerBox.AnchorLeft = 0;
        _bannerBox.AnchorRight = 1;
        _bannerBox.AnchorTop = 0.36f;
        _bannerBox.AddChild(_banner);
        _bannerBox.AddChild(_bannerSub);
        _bannerBox.Visible = false;
        root.AddChild(_bannerBox);

        _netPanel.AnchorLeft = 1;
        _netPanel.AnchorRight = 1;
        _netPanel.AnchorTop = 1;
        _netPanel.AnchorBottom = 1;
        _netPanel.GrowHorizontal = Control.GrowDirection.Begin;
        _netPanel.GrowVertical = Control.GrowDirection.Begin;
        _netPanel.OffsetRight = -16;
        _netPanel.OffsetBottom = -16;
        _netPanel.AddChild(_netGraph);
        _netPanel.Visible = false;
        root.AddChild(_netPanel);
    }

    public void Refresh(GameClient client, double frameDt, bool showScores, ServerStatsSource? serverStats)
    {
        var snap = client.LatestSnapshot;
        if (snap is null) return;
        var mode = client.MatchInfo?.Mode ?? GameMode.Coop;
        var me = (uint)client.Slot < (uint)snap.Players.Length ? snap.Players[client.Slot] : default;

        _left.Text = mode == GameMode.Coop
            ? $"♥ {me.Lives}   ★ {StarsOf(client)}   {me.Score:N0} 分"
            : $"★ {StarsOf(client)}   击杀 {me.Kills}   阵亡 {me.Deaths}";
        _left.AddThemeColorOverride("font_color", mode == GameMode.BaseBattle ? Palette.ForTeam(me.Team) : Palette.ForSlot(client.Slot));
        _center.Text = mode switch
        {
            GameMode.Coop => $"剩余敌军  {snap.EnemiesRemaining}",
            GameMode.Deathmatch => $"{snap.TicksRemaining / Units.TickRate / 60}:{snap.TicksRemaining / Units.TickRate % 60:00}",
            _ => "摧毁敌方老鹰",
        };
        _right.Text = Scoreboard(client, snap, mode, showScores);

        bool ended = snap.Phase == MatchPhase.Ended;
        _bannerBox.Visible = ended;
        if (ended) SetBanner(client, snap, mode);

        if (NetGraphVisible) _netGraph.Text = NetGraph(client, frameDt, serverStats);
    }

    private static int StarsOf(GameClient client) => client.Predictor.HasTank ? client.Predictor.Current.Stars : 0;

    private static string Scoreboard(GameClient client, Snapshot snap, GameMode mode, bool full)
    {
        var names = client.Lobby?.Players.ToDictionary(p => (int)p.Slot, p => p.Name) ?? new Dictionary<int, string>();
        var rows = Enumerable.Range(0, snap.Players.Length)
            .Where(i => snap.Players[i].Active)
            .OrderByDescending(i => mode == GameMode.Coop ? snap.Players[i].Score : snap.Players[i].Kills)
            .Take(full ? 8 : 4)
            .Select(i =>
            {
                var p = snap.Players[i];
                string name = names.GetValueOrDefault(i, $"P{i + 1}");
                string value = mode == GameMode.Coop ? $"{p.Score,6:N0}" : $"{p.Kills,3}";
                return $"{(i == client.Slot ? "▶ " : "")}{UiKit.Pad(name, 12)}{value}";
            });
        return string.Join("\n", rows);
    }

    private void SetBanner(GameClient client, Snapshot snap, GameMode mode)
    {
        byte myTeam = client.Predictor.HasTank ? client.Predictor.Current.Team : snap.Players[client.Slot].Team;
        bool won = snap.WinnerTeam == myTeam;
        string text = mode switch
        {
            GameMode.Coop => won ? "关卡完成" : "游戏结束",
            GameMode.BaseBattle => won ? "胜利" : "失败",
            _ => won ? "你赢了" : $"{client.Lobby?.Players.FirstOrDefault(p => p.Slot == snap.WinnerTeam)?.Name ?? "?"} 获胜",
        };
        _banner.Text = text;
        _banner.AddThemeColorOverride("font_color", won ? Palette.UiAccent : Palette.UiDanger);
        _bannerSub.Text = client.IsHost ? "几秒后返回房间…" : "正在返回房间…";
    }

    private string NetGraph(GameClient client, double frameDt, ServerStatsSource? server)
    {
        _frameTimes.Add(frameDt);
        if (_frameTimes.Count > 120) _frameTimes.RemoveAt(0);
        double avg = _frameTimes.Average(), worst = _frameTimes.Max();
        var s = client.Stats;
        string serverLine = server is null ? "" : $"\n服务器帧耗时  平均 {server.AverageTickMs:F2} ms  最大 {server.MaxTickMs:F2} ms";
        return
            $"帧率      {1.0 / avg,5:F0}   最慢一帧 {worst * 1000:F1} ms\n" +
            $"延迟      {s.RttMs,5} ms  抖动 {s.JitterMs:F1} ms\n" +
            $"丢包      {s.PacketLossPercent,5:F1} %\n" +
            $"插值      {s.InterpDelayMs,5:F0} ms  外推 {s.Extrapolations}\n" +
            $"快照      {s.SnapshotsPerSecond,5} /s\n" +
            $"下行/上行 {s.BytesInPerSecond / 1024,5:F1} / {s.BytesOutPerSecond / 1024:F1} KB/s\n" +
            $"预测校正  {s.Mispredictions,5}   地图重同步 {s.MapResyncs}\n" +
            $"输入余量  {s.InputSlackTicks,5:F1} 帧  时钟 x{s.ClockRate:F3}" +
            serverLine;
    }
}

/// <summary>Server timing, available only on the hosting machine.</summary>
public sealed class ServerStatsSource
{
    private readonly Func<(double Avg, double Max)> _read;

    public ServerStatsSource(Func<(double Avg, double Max)> read) => _read = read;

    public double AverageTickMs => _read().Avg;
    public double MaxTickMs => _read().Max;
}
