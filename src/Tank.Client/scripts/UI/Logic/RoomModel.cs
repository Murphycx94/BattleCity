using System.Globalization;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Client.UI;

/// <summary>How a row's state text should be coloured; the screen picks the actual colours.</summary>
public enum RowTone { Normal, Dim, Good }

/// <summary>Whose side a column is, seen from the viewer's seat: teams are never "gold" or "blue", only "mine" and "theirs".</summary>
public enum ColumnSide { Mine, Other, Everyone }

/// <param name="Ghost">A placeholder for bots the server will add when the match starts, not a seat yet.</param>
public sealed record RoomRowView(byte Slot, string Name, string Tags, string TankName, string State, RowTone Tone, bool IsMe, bool IsBot, bool Ghost);

/// <param name="Title">Empty for the second column of a mode without teams: the screen draws no title there but leaves its height, so the columns' rows line up.</param>
public sealed record RoomColumnView(string Title, ColumnSide Side, IReadOnlyList<RoomRowView> Rows);

/// <summary>One line of the host's mode list: why the room can't switch to <paramref name="Mode"/> now (null: it can).</summary>
public sealed record ModeChoice(BattleMode Mode, string Name, string? Blocked);

/// <summary>
/// Everything the room screen shows and which controls work, worked out from the room state and the viewer's slot.
/// Engine-free so it is tested as it is. In a team mode there are two columns, 我方 (the viewer's team) and 敌方; without
/// teams, one — or two once there are more than <see cref="SoloColumnRows"/> rows (a royale seats 16, stage 6 design §2),
/// the second untitled. The host alone sets mode, map and bots and starts; everyone picks a tank, readies up and (in a
/// team mode) swaps sides. A mode the room holds too many people for is in the list but blocked, with the reason.
/// </summary>
public sealed class RoomModel
{
    /// <summary>The rows a mode without teams shows in one column; more go in two.</summary>
    public const int SoloColumnRows = 8;

    /// <summary>Under the map, when the mode fixes it (a royale: the riverland).</summary>
    public const string MapLockedHint = "吃鸡只在河谷开";

    private const string WaitingNotice = "对局进行中，等下一局";
    private const string BotsNote = "机器人";
    private const string EveryoneTitle = "玩家";

    private string? _signature;

    private RoomModel() { }

    public BattleMode Mode { get; private init; }
    public int Bots { get; private init; }

    /// <summary>The most bots the room's mode seats (<see cref="ModeRules.MaxBotsFor"/>): 15 in a royale, 9 otherwise.</summary>
    public int MaxBots { get; private init; }

    /// <summary>Every mode in <see cref="ModeRules.All"/>'s order, each saying why the room can't switch to it (the current one never is).</summary>
    public IReadOnlyList<ModeChoice> ModeChoices { get; private init; } = Array.Empty<ModeChoice>();

    /// <summary><see cref="MapLockedHint"/> when the mode is played on one map only, else null.</summary>
    public string? MapHint { get; private init; }
    public bool HasTeams { get; private init; }
    public bool IsHost { get; private init; }
    /// <summary>The mode and the map, e.g. 团队对战 · 河谷.</summary>
    public string Title { get; private init; } = "";
    public string RuleLine { get; private init; } = "";

    /// <summary>Set while a match is running and the viewer sits in the room: they join the next one.</summary>
    public string? WaitingText { get; private init; }

    public IReadOnlyList<RoomColumnView> Columns { get; private init; } = Array.Empty<RoomColumnView>();

    /// <summary>The room's map, a <c>MapCatalog</c> id as the server sent it.</summary>
    public string MapId { get; private init; } = "";

    /// <summary>The map's display name (试驾场地图, 河谷).</summary>
    public string MapName { get; private init; } = "";

    /// <summary>Whether the server has the map ready; only then can a match start.</summary>
    public MapState MapState { get; private init; }

    public bool CanSetMode { get; private init; }
    public bool CanSetBots { get; private init; }
    public bool CanSetMap { get; private init; }
    public bool CanSwitchTeam { get; private init; }

    /// <summary>The team to ask for with 换到对面: the one the viewer isn't on.</summary>
    public byte OtherTeam { get; private init; }

    /// <summary>The viewer's tank as an index into <see cref="TankCatalog.All"/>; -1 when the viewer isn't in the list.</summary>
    public int MyTankIndex { get; private init; } = -1;

    public bool MyReady { get; private init; }
    public string ReadyLabel => MyReady ? "取消准备" : "准备";

    public bool ShowStart { get; private init; }
    public bool CanStart { get; private init; }

    /// <summary>Under the Start button: why it is disabled, or how many are ready.</summary>
    public string StartHint { get; private init; } = "";

    /// <summary>Changes whenever anything on screen would, so the screen rebuilds only then.</summary>
    public string Signature => _signature ??= SignatureOf(this);

    public static RoomModel Build(RoomStateMessage room, byte mySlot)
    {
        RoomPlayer? me = room.Players.FirstOrDefault(p => p.Slot == mySlot);
        bool teams = ModeRules.HasTeams(room.Mode);
        bool isHost = room.HostSlot == mySlot;
        byte myTeam = me?.Team ?? 0;
        StartPlan plan = room.MatchRunning ? default : RoomRules.PlanFor(room);   // bots join only when a match starts
        string? block = isHost ? RoomRules.StartBlockReason(room) : null;
        string mapName = MapText.Name(room.Map);
        bool mapFixed = ModeRules.OnlyMap(room.Mode) is not null;
        return new RoomModel
        {
            Mode = room.Mode,
            Bots = room.Bots,
            MaxBots = ModeRules.MaxBotsFor(room.Mode),
            ModeChoices = ChoicesFor(room),
            MapHint = mapFixed ? MapLockedHint : null,
            HasTeams = teams,
            IsHost = isHost,
            Title = string.Create(CultureInfo.InvariantCulture, $"{ModeText.Name(room.Mode)} · {mapName}"),
            RuleLine = ModeText.Rule(room.Mode),
            WaitingText = room.MatchRunning ? WaitingNotice : null,
            Columns = BuildColumns(room, mySlot, myTeam, teams, plan),
            MapId = room.Map,
            MapName = mapName,
            MapState = room.MapState,
            CanSetMode = isHost && !room.MatchRunning,
            CanSetBots = isHost && !room.MatchRunning,
            CanSetMap = isHost && !room.MatchRunning && !mapFixed,
            CanSwitchTeam = teams && me is not null,
            OtherTeam = (byte)(1 - myTeam),
            MyTankIndex = me is null ? -1 : me.TankIndex,
            MyReady = me?.Ready ?? false,
            ShowStart = isHost,
            CanStart = isHost && block is null,
            StartHint = isHost ? block ?? ReadyText(room) : "",
        };
    }

    private static string ReadyText(RoomStateMessage room)
    {
        int people = room.Players.Count(p => !p.IsBot);
        int ready = room.Players.Count(p => !p.IsBot && p.Ready);
        return string.Create(CultureInfo.InvariantCulture, $"{ready}/{people} 人已准备，随时可以开始。");
    }

    /// <summary>
    /// The modes as the host's list shows them: one the room holds more people than it seats (bots don't count, a dropped
    /// player's seat does) is blocked with the reason, as the server would refuse it; the room's own mode never is.
    /// </summary>
    private static IReadOnlyList<ModeChoice> ChoicesFor(RoomStateMessage room)
    {
        int humans = room.Players.Count(p => !p.IsBot);
        var choices = new ModeChoice[ModeRules.All.Count];
        for (int i = 0; i < choices.Length; i++)
        {
            BattleMode mode = ModeRules.All[i];
            int seats = ModeRules.MaxPlayersFor(mode);
            string? blocked = mode != room.Mode && humans > seats
                ? string.Create(CultureInfo.InvariantCulture, $"真人超过 {seats} 个，不能换到{ModeText.Name(mode)}。")
                : null;
            choices[i] = new ModeChoice(mode, ModeText.Name(mode), blocked);
        }
        return choices;
    }

    private static IReadOnlyList<RoomColumnView> BuildColumns(RoomStateMessage room, byte mySlot, byte myTeam, bool teams, StartPlan plan)
    {
        IEnumerable<RoomPlayer> ordered = room.Players.OrderBy(p => p.Slot);
        RoomRowView Row(RoomPlayer p) => RowOf(p, room.HostSlot, mySlot);
        if (!teams)
        {
            List<RoomRowView> rows = ordered.Select(Row).ToList();
            int everyone = rows.Count + plan.Bots;
            AddGhost(rows, plan.Bots);
            string title = Titled(EveryoneTitle, everyone);
            if (rows.Count <= SoloColumnRows) return new[] { new RoomColumnView(title, ColumnSide.Everyone, rows) };
            int first = (rows.Count + 1) / 2;   // the first column takes the extra row
            return new[]
            {
                new RoomColumnView(title, ColumnSide.Everyone, rows.GetRange(0, first)),
                new RoomColumnView("", ColumnSide.Everyone, rows.GetRange(first, rows.Count - first)),
            };
        }
        List<RoomRowView> mine = ordered.Where(p => p.Team == myTeam).Select(Row).ToList();
        List<RoomRowView> other = ordered.Where(p => p.Team != myTeam).Select(Row).ToList();
        bool mineIsTeam0 = myTeam == 0;
        int botsMine = mineIsTeam0 ? plan.BotsToTeam0 : plan.BotsToTeam1;
        int botsOther = mineIsTeam0 ? plan.BotsToTeam1 : plan.BotsToTeam0;
        int tanksMine = mine.Count + botsMine, tanksOther = other.Count + botsOther;   // the title counts tanks, bots to come included
        AddGhost(mine, botsMine);
        AddGhost(other, botsOther);
        return new[]
        {
            new RoomColumnView(Titled("我方", tanksMine), ColumnSide.Mine, mine),
            new RoomColumnView(Titled("敌方", tanksOther), ColumnSide.Other, other),
        };
    }

    private static string Titled(string name, int count) => string.Create(CultureInfo.InvariantCulture, $"{name} · {count}");

    private static void AddGhost(List<RoomRowView> rows, int bots)
    {
        if (bots <= 0) return;
        rows.Add(new RoomRowView(byte.MaxValue, string.Create(CultureInfo.InvariantCulture, $"{BotsNote} × {bots}"), "", "", "开局时补入", RowTone.Dim, false, true, true));
    }

    private static RoomRowView RowOf(RoomPlayer p, byte hostSlot, byte mySlot)
    {
        string tags = string.Join(' ', new[] { p.Slot == hostSlot ? "房主" : null, p.Slot == mySlot ? "你" : null }.OfType<string>());
        (string state, RowTone tone) = p switch
        {
            { IsBot: true } => (BotsNote, RowTone.Dim),
            { Connected: false } => ("重连中", RowTone.Dim),
            { Ready: true } => ("已准备", RowTone.Good),
            _ => ("未准备", RowTone.Dim),
        };
        return new RoomRowView(p.Slot, p.Name, tags, ModeText.TankName(p.TankIndex), state, tone, p.Slot == mySlot, p.IsBot, false);
    }

    private static string SignatureOf(RoomModel m)
    {
        IEnumerable<string> rows = m.Columns.SelectMany(c => c.Rows.Select(r => $"{c.Side}:{r.Slot}:{r.Name}:{r.Tags}:{r.TankName}:{r.State}"));
        IEnumerable<string?> blocked = m.ModeChoices.Select(c => c.Blocked);
        return string.Create(CultureInfo.InvariantCulture,
            $"{m.Mode}|{m.MapId}|{m.MapState}|{m.Bots}|{m.MaxBots}|{m.IsHost}|{m.WaitingText}|{m.CanStart}|{m.StartHint}|{m.MyTankIndex}|{m.MyReady}|{m.MapHint}|{string.Join(',', blocked)}|{m.Columns.Count}|{string.Join(';', rows)}");
    }
}
