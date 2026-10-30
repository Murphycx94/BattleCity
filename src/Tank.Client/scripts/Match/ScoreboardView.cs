using System.Globalization;
using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The scoreboard (blueprint §4.2): a header (名次 / 名字 / 车型 / 击毁 / 阵亡 / 状态, and in a standard battle 占领 before
/// 状态, stage 5 design §3.4, with the kills and deaths moved left to make room; in a royale 伤害 in place of 阵亡, stage 6
/// design §3.3, and once it has ended 存活 — how long each tank lasted — in place of 状态), in the team modes a banner per team
/// (我方 first, then 敌方, in the sides' colours) and one line per player, the player's own tinted in the accent colour
/// and the fallen dimmed. An enemy's 占领 cell stays blank until the match has ended (the server hides it till then), and so does
/// another player's 伤害. A board of more than ten lines (a royale's sixteen) uses <see cref="ScoreboardMetrics.Compact"/>'s row height and
/// type sizes so it fits the same box; the ten-seat modes are drawn exactly as before. It shows while Tab is held and, in the same shape at the results position, once the match has
/// ended. The lines are rebuilt only when the scoreboard's numbers, the names or the own respawn second change, and the
/// view redraws only then (or when shown or resized): nothing here allocates on a quiet frame.
/// </summary>
public partial class ScoreboardView : HudPanel
{
    private const int HeaderSize = 20, TeamSize = 22, TextOutline = 4;
    private const float RankX = 64f, NameX = 112f, TankX = 350f;

    /// <summary>Where the number columns and the status sit: the other modes' five columns, standard's with 占领, or a royale's with 伤害 (<see cref="DamageX"/>) and no 阵亡.</summary>
    private readonly record struct Columns(float Kill, float Death, float Capture, float Status);

    private static readonly Columns Plain = new(Kill: 600f, Death: 685f, Capture: 0f, Status: 745f);
    private static readonly Columns WithCapture = new(Kill: 560f, Death: 640f, Capture: 720f, Status: 790f);
    private static readonly Columns Royale = new(Kill: 560f, Death: 0f, Capture: 0f, Status: 790f);
    private const float DamageX = 660f;
    private const float NameWidth = TankX - NameX - 12f;
    private const int PanelRadius = 24, RowRadius = 12;
    private const float PanelAlpha = 0.88f, TeamAlpha = 0.3f, OwnAlpha = 0.3f, DeadAlpha = 0.5f, DividerInset = 18f;

    private static readonly string[] Headers = { "名次", "名字", "车型", "击毁", "阵亡", "状态" };
    private const string CaptureHeader = "占领", DamageHeader = "伤害", LastedHeader = "存活";
    private static readonly Func<string, float> MeasureRegular = text => HudStyle.TextWidth(text, ScoreboardMetrics.Regular.BodySize);   // cached: one delegate, not one per name
    private static readonly Func<string, float> MeasureCompact = text => HudStyle.TextWidth(text, ScoreboardMetrics.Compact.BodySize);

    private struct Line
    {
        public string Rank, Name, Tank, Kills, Deaths, Capture, Damage, Lasted, Status;
        public Color NameColor;
        public bool Own, Dim, TeamStart, FirstGroup;
    }

    private readonly ScoreRow[] _rows = new ScoreRow[BattleProtocol.MaxPlayers];
    private readonly Line[] _lines = new Line[BattleProtocol.MaxPlayers];
    private readonly PlayerStats[] _seenPlayers = new PlayerStats[BattleProtocol.MaxPlayers];
    private readonly string?[] _seenNames = new string?[BattleProtocol.MaxPlayers];
    private readonly Func<byte, string?> _nameOf;
    private readonly StyleBoxFlat _panel = HudStyle.RoundedBox(new Color(HudStyle.Panel, PanelAlpha), PanelRadius, 8);
    private readonly StyleBoxFlat _ownRow = HudStyle.RoundedBox(new Color(HudStyle.Accent, OwnAlpha), RowRadius, 6);
    private readonly StyleBoxFlat _allyBanner = HudStyle.RoundedBox(new Color(MatchColors.Ui(Side.Ally).ToColor(), TeamAlpha), RowRadius, 6);
    private readonly StyleBoxFlat _enemyBanner = HudStyle.RoundedBox(new Color(MatchColors.Ui(Side.Enemy).ToColor(), TeamAlpha), RowRadius, 6);
    private string?[] _names = Array.Empty<string?>();
    private int _count, _groups;
    private bool _hasLines, _teams, _results, _capture, _royale;
    private ScoreboardMetrics _metrics = ScoreboardMetrics.Regular;
    private BattleMode _mode;
    private byte _ownSlot, _ownTeam;
    private bool _ended;
    private int _respawnSeconds;

    public ScoreboardView()
    {
        _nameOf = slot => slot < _names.Length ? _names[slot] : null;
        Visible = false;
    }

    /// <summary>Call once a frame. <paramref name="tabHeld"/> shows the board; so does the end of the match (then at the results position).</summary>
    public void Update(in MatchHudFrame frame, bool tabHeld)
    {
        bool ended = frame.Phase == MatchPhase.Ended;
        if (!tabHeld && !ended)
        {
            Visible = false;   // the lines are left as they were: the next show compares them with the frame's and rebuilds if they differ
            return;
        }
        bool wasVisible = Visible;
        Visible = true;
        int respawnSeconds = HudText.CeilSeconds(frame.RespawnIn);
        bool changed = !wasVisible || ended != _results;
        if (!_hasLines || Differs(frame, ended, respawnSeconds))
        {
            Rebuild(frame, ended, respawnSeconds);
            changed = true;
        }
        _results = ended;
        if (changed) QueueRedraw();
    }

    private bool Differs(in MatchHudFrame frame, bool ended, int respawnSeconds)
    {
        if (frame.Mode != _mode || frame.OwnSlot != _ownSlot || frame.OwnTeam != _ownTeam || ended != _ended || respawnSeconds != _respawnSeconds) return true;
        for (int i = 0; i < _seenPlayers.Length; i++)
            if (!_seenPlayers[i].Equals(frame.Players[i]) || !ReferenceEquals(_seenNames[i], frame.Names[i])) return true;
        return false;
    }

    private void Rebuild(in MatchHudFrame frame, bool ended, int respawnSeconds)
    {
        (_mode, _ownSlot, _ownTeam, _ended, _respawnSeconds, _hasLines) = (frame.Mode, frame.OwnSlot, frame.OwnTeam, ended, respawnSeconds, true);
        Array.Copy(frame.Players, _seenPlayers, _seenPlayers.Length);
        Array.Copy(frame.Names, _seenNames, _seenNames.Length);
        _names = frame.Names;
        _teams = ModeRules.HasTeams(frame.Mode);
        _capture = frame.Mode == BattleMode.Standard;
        _royale = frame.Mode == BattleMode.Royale;
        _count = ScoreboardModel.Build(frame.Mode, frame.OwnSlot, frame.OwnTeam, frame.Players, _nameOf, ended, frame.RespawnIn, _rows);
        _metrics = ScoreboardMetrics.For(_count);
        Func<string, float> measure = _metrics == ScoreboardMetrics.Compact ? MeasureCompact : MeasureRegular;
        _groups = 0;
        for (int i = 0; i < _count; i++)
        {
            ScoreRow row = _rows[i];
            bool start = _teams && (i == 0 || row.Team != _rows[i - 1].Team);
            if (start) _groups++;
            _lines[i] = new Line
            {
                Rank = Number(row.Rank), Name = Fit(row.Name, measure), Tank = row.TankName, Kills = Number(row.Kills), Deaths = Number(row.Deaths),
                Capture = _capture ? ScoreboardModel.CaptureCell(row, ended) : string.Empty,
                Damage = _royale ? ScoreboardModel.DamageCell(row, ended) : string.Empty,
                Lasted = _royale && ended ? ScoreboardModel.LastedCell(row) : string.Empty,
                Status = row.Status, NameColor = NameColor(row), Own = row.Slot == frame.OwnSlot, Dim = !row.Alive,
                TeamStart = start, FirstGroup = _groups == 1,
            };
        }
    }

    private Color NameColor(in ScoreRow row) => _teams ? MatchColors.Ui(row.Side).ToColor() : row.Side == Side.Own ? HudStyle.Accent : HudStyle.Text;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The name cut to the name column, with an ellipsis where it was cut.</summary>
    private static string Fit(string name, Func<string, float> measure) => HudText.Fit(name, NameWidth, measure);

    private Columns At => _royale ? Royale : _capture ? WithCapture : Plain;

    public override void _Draw()
    {
        if (!_hasLines) return;
        HudRect table = _results ? Boxes.ResultsTable : Boxes.Scoreboard;
        DrawStyleBox(_panel, new Rect2(table.X, table.Y, table.Width, _metrics.Height(_groups, _count)));
        DrawHeader(table);
        float y = table.Y + ScoreboardMetrics.HeaderHeight;
        for (int i = 0; i < _count; i++)
        {
            ref readonly Line line = ref _lines[i];
            if (line.TeamStart)
            {
                DrawTeamBanner(table, y, line.FirstGroup);
                y += ScoreboardMetrics.TeamBannerHeight;
            }
            DrawPlayerLine(table, y, line);
            y += _metrics.RowHeight;
        }
    }

    private void DrawHeader(HudRect table)
    {
        float baseline = table.Y + HudStyle.Baseline(ScoreboardMetrics.HeaderHeight / 2, HeaderSize);
        Color dim = Palette.UiDim.ToColor();
        Columns at = At;
        HudStyle.DrawOutlinedCentered(this, table.X + RankX, baseline, Headers[0], HeaderSize, dim, TextOutline);
        HudStyle.DrawOutlined(this, new Vector2(table.X + NameX, baseline), Headers[1], HeaderSize, dim, TextOutline);
        HudStyle.DrawOutlined(this, new Vector2(table.X + TankX, baseline), Headers[2], HeaderSize, dim, TextOutline);
        HudStyle.DrawOutlinedCentered(this, table.X + at.Kill, baseline, Headers[3], HeaderSize, dim, TextOutline);
        if (_royale) HudStyle.DrawOutlinedCentered(this, table.X + DamageX, baseline, DamageHeader, HeaderSize, dim, TextOutline);
        else HudStyle.DrawOutlinedCentered(this, table.X + at.Death, baseline, Headers[4], HeaderSize, dim, TextOutline);
        if (_capture) HudStyle.DrawOutlinedCentered(this, table.X + at.Capture, baseline, CaptureHeader, HeaderSize, dim, TextOutline);
        HudStyle.DrawOutlined(this, new Vector2(table.X + at.Status, baseline), _royale && _results ? LastedHeader : Headers[5], HeaderSize, dim, TextOutline);
        float rule = table.Y + ScoreboardMetrics.HeaderHeight - 2;
        DrawLine(new Vector2(table.X + DividerInset, rule), new Vector2(table.Right - DividerInset, rule), new Color(HudStyle.OutlineColor, 0.8f), 2f);
    }

    private void DrawTeamBanner(HudRect table, float y, bool own)
    {
        var box = new Rect2(table.X + DividerInset / 2, y + 3, table.Width - DividerInset, ScoreboardMetrics.TeamBannerHeight - 6);
        DrawStyleBox(own ? _allyBanner : _enemyBanner, box);
        Color color = MatchColors.Ui(own ? Side.Ally : Side.Enemy).ToColor();
        HudStyle.DrawOutlined(this, new Vector2(box.Position.X + 16, y + HudStyle.Baseline(ScoreboardMetrics.TeamBannerHeight / 2, TeamSize)), own ? "我方" : "敌方", TeamSize, color, TextOutline);
    }

    private void DrawPlayerLine(HudRect table, float y, in Line line)
    {
        float rowHeight = _metrics.RowHeight;
        int bodySize = _metrics.BodySize, statusSize = _metrics.StatusSize;
        if (line.Own) DrawStyleBox(_ownRow, new Rect2(table.X + DividerInset / 2, y + 2, table.Width - DividerInset, rowHeight - 4));
        float baseline = y + HudStyle.Baseline(rowHeight / 2, bodySize);
        float statusBaseline = y + HudStyle.Baseline(rowHeight / 2, statusSize);
        float alpha = line.Dim ? DeadAlpha : 1f;
        Color text = new(HudStyle.Text, alpha), name = new(line.NameColor, alpha), dim = new(Palette.UiDim.ToColor(), alpha);
        Columns at = At;
        HudStyle.DrawOutlinedCentered(this, table.X + RankX, baseline, line.Rank, bodySize, text, TextOutline);
        HudStyle.DrawOutlined(this, new Vector2(table.X + NameX, baseline), line.Name, bodySize, name, TextOutline);
        HudStyle.DrawOutlined(this, new Vector2(table.X + TankX, baseline), line.Tank, bodySize, dim, TextOutline);
        HudStyle.DrawOutlinedCentered(this, table.X + at.Kill, baseline, line.Kills, bodySize, text, TextOutline);
        if (_royale)
        {
            if (line.Damage.Length > 0) HudStyle.DrawOutlinedCentered(this, table.X + DamageX, baseline, line.Damage, bodySize, text, TextOutline);
        }
        else
        {
            HudStyle.DrawOutlinedCentered(this, table.X + at.Death, baseline, line.Deaths, bodySize, text, TextOutline);
        }
        if (line.Capture.Length > 0) HudStyle.DrawOutlinedCentered(this, table.X + at.Capture, baseline, line.Capture, bodySize, text, TextOutline);
        if (line.Lasted.Length > 0) HudStyle.DrawOutlined(this, new Vector2(table.X + at.Status, baseline), line.Lasted, bodySize, text, TextOutline);
        else HudStyle.DrawOutlined(this, new Vector2(table.X + at.Status, statusBaseline), line.Status, statusSize, StatusColor(line, alpha), TextOutline);
    }

    private static Color StatusColor(in Line line, float alpha) =>
        line.Dim ? new Color(Palette.UiDanger.ToColor(), alpha) : new Color(Palette.UiGood.ToColor(), alpha);
}
