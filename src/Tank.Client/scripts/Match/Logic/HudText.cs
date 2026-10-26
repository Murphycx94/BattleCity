using System.Globalization;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>What the score plate's two side blocks count (blueprint §4.2): kills per team, tanks left per team, or the player's own kills and the field.</summary>
public enum PlateKind : byte { TeamKills, TeamAlive, SoloKills }

/// <summary>
/// The numbers behind the score plate's side blocks. <c>Left</c> is the player's side (own team, or the player's own
/// kills); <c>RightOf</c> and <c>LeftOf</c> are 0 where the block shows no "of". A plain value, so a view can tell a change by comparing.
/// </summary>
public readonly record struct PlateValues(PlateKind Kind, int Left, int LeftOf, int Right, int RightOf);

/// <summary>The words on the two side blocks: a small caption over a big value.</summary>
public readonly record struct PlateParts(string LeftCaption, string LeftValue, string RightCaption, string RightValue);

/// <summary>How the match went for the player, for the colour of the results title.</summary>
public enum ResultTone : byte { Neutral, Win, Lose }

/// <summary>The results screen's title and the line under it.</summary>
public readonly record struct ResultsParts(string Title, string Subtitle, ResultTone Tone);

/// <summary>
/// The HUD's words and sums, none of them touching the engine: the match clock (rounded up, so the last second reads 0:01
/// and 0:00 means over), the respawn line, and the score plate per mode. Strings are built here, so a view calls them
/// only when the numbers behind them changed.
/// </summary>
public static class HudText
{
    private const string TeamKillsLeft = "我方", TeamKillsRight = "敌方";
    private const string TeamAliveLeft = "我方存活", TeamAliveRight = "敌方存活";
    private const string SoloKills = "击毁", SoloAlive = "存活";

    /// <summary>What a name cut by <see cref="Fit"/> ends with.</summary>
    public const string Ellipsis = "…";

    /// <summary>Whole seconds in <paramref name="ticks"/>, rounded up: the clock shows a second until it has all gone.</summary>
    public static int CeilSeconds(int ticks) => ticks <= 0 ? 0 : (ticks + SimConstants.TickRate - 1) / SimConstants.TickRate;

    /// <summary>"m:ss" for the time left, rounding up to the next second.</summary>
    public static string Timer(int ticksLeft) => Clock(CeilSeconds(ticksLeft));

    /// <summary>"m:ss" for a whole number of seconds.</summary>
    public static string Clock(int seconds)
    {
        if (seconds < 0) seconds = 0;
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>"N 秒后复活" for the ticks until the own tank is back (rounded up, so it never reads 0 while waiting).</summary>
    public static string RespawnLine(int respawnIn) =>
        string.Create(CultureInfo.InvariantCulture, $"{CeilSeconds(respawnIn)} 秒后复活");

    /// <summary>The big countdown number of the respawn banner.</summary>
    public static string RespawnNumber(int respawnIn) =>
        CeilSeconds(respawnIn).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// <paramref name="text"/> cut to <paramref name="budget"/> as <paramref name="width"/> measures it (the view's font at
    /// its size), with <see cref="Ellipsis"/> where it was cut: whole characters only (a surrogate pair stays together),
    /// at least one kept. A text that fits, or is a single character, comes back as it is, so only a long one allocates.
    /// </summary>
    public static string Fit(string text, float budget, Func<string, float> width)
    {
        if (text.Length <= 1 || width(text) <= budget) return text;
        int length = text.Length;
        string cut;
        do
        {
            length--;
            if (length > 0 && char.IsLowSurrogate(text[length])) length--;   // cut before the pair, not inside it
            cut = string.Concat(text.AsSpan(0, length), Ellipsis);
        }
        while (length > 1 && width(cut) > budget);
        return cut;
    }

    /// <summary>"观战 name · Q/E 切换", the spectating bar's text.</summary>
    public static string SpectateLine(string target) => $"观战 {target} · Q/E 切换";

    /// <summary>The spectating bar while the camera is free (a free-for-all with nobody left to follow, or F).</summary>
    public const string SpectateFreeLine = "自由视角 · WASD 移动 · Q/E 跟随";

    /// <summary>
    /// What the plate shows for <paramref name="mode"/>. Team battle: the team kills from <paramref name="match"/>, own
    /// team first. Annihilation and standard: tanks alive of all each side has had in the match (those who joined
    /// midway included). Free-for-all: the player's own kills and the tanks still alive of the field.
    /// <paramref name="players"/> is indexed by room slot.
    /// </summary>
    public static PlateValues Plate(BattleMode mode, byte ownSlot, byte ownTeam, in MatchBlock match, ReadOnlySpan<PlayerStats> players)
    {
        switch (mode)
        {
            case BattleMode.TeamBattle:
                bool first = ownTeam == 0;
                return new PlateValues(PlateKind.TeamKills, first ? match.TeamScore0 : match.TeamScore1, 0, first ? match.TeamScore1 : match.TeamScore0, 0);
            case BattleMode.Annihilation:
            case BattleMode.Standard:
                Count(players, ownTeam, out int mine, out int mineOf);
                Count(players, (byte)(1 - ownTeam), out int theirs, out int theirsOf);
                return new PlateValues(PlateKind.TeamAlive, mine, mineOf, theirs, theirsOf);
            default:
                int kills = ownSlot < players.Length ? players[ownSlot].Kills : 0;
                CountAll(players, out int alive, out int total);
                return new PlateValues(PlateKind.SoloKills, kills, 0, alive, total);
        }
    }

    /// <summary>The captions and values for <paramref name="plate"/>; allocates, so call it when the values changed.</summary>
    public static PlateParts Parts(in PlateValues plate) => plate.Kind switch
    {
        PlateKind.TeamKills => new PlateParts(TeamKillsLeft, Number(plate.Left), TeamKillsRight, Number(plate.Right)),
        PlateKind.TeamAlive => new PlateParts(TeamAliveLeft, Fraction(plate.Left, plate.LeftOf), TeamAliveRight, Fraction(plate.Right, plate.RightOf)),
        _ => new PlateParts(SoloKills, Number(plate.Left), SoloAlive, Fraction(plate.Right, plate.RightOf)),
    };

    /// <summary>
    /// The results screen's words (allocates: call it when its inputs change). Team modes: 胜利 / 失败 / 平局 from the
    /// winning team against <paramref name="ownTeam"/>, with the kills, the wipe-out or (standard) why the match ended
    /// (<see cref="ResultsInfo.Reason"/>) under it. Free-for-all: the player's
    /// place ("第 N 名", from the server's rank) and who was left. <paramref name="plate"/> and <paramref name="players"/>
    /// are the frame's; <paramref name="names"/> is by slot.
    /// </summary>
    public static ResultsParts Results(BattleMode mode, byte ownSlot, byte ownTeam, in ResultsInfo info, in PlateValues plate, ReadOnlySpan<PlayerStats> players, string?[] names)
    {
        if (!ModeRules.HasTeams(mode)) return SoloResults(ownSlot, info, players, names);
        ResultTone tone = info.WinnerTeam == MatchRules.NoTeam ? ResultTone.Neutral : info.WinnerTeam == ownTeam ? ResultTone.Win : ResultTone.Lose;
        string title = tone switch { ResultTone.Win => "胜利", ResultTone.Lose => "失败", _ => "平局" };
        string subtitle = mode switch
        {
            BattleMode.TeamBattle => string.Create(CultureInfo.InvariantCulture, $"击毁 {plate.Left} : {plate.Right}"),
            BattleMode.Standard => StandardLine(tone, info.Reason),
            _ => AnnihilationLine(tone, plate),
        };
        return new ResultsParts(title, subtitle, tone);
    }

    private static string AnnihilationLine(ResultTone tone, in PlateValues plate) => tone switch
    {
        ResultTone.Win => "敌方全军覆没",
        ResultTone.Lose => "我方全军覆没",
        _ => plate.Left == 0 && plate.Right == 0 ? "双方同归于尽" : "时间到，胜负未分",
    };

    private static string StandardLine(ResultTone tone, MatchEndReason reason) => (tone, reason) switch
    {
        (ResultTone.Win, MatchEndReason.Captured) => "占领了敌方基地",
        (ResultTone.Win, _) => "全歼敌人",
        (ResultTone.Lose, MatchEndReason.Captured) => "我方基地被占领",
        (ResultTone.Lose, _) => "我方全灭",
        (_, MatchEndReason.Eliminated) => "双方同归于尽",
        (_, MatchEndReason.Captured) => "双方同时占领了对方基地",
        _ => "时间到，平局",
    };

    private static ResultsParts SoloResults(byte ownSlot, in ResultsInfo info, ReadOnlySpan<PlayerStats> players, string?[] names)
    {
        int rank = ownSlot < players.Length ? players[ownSlot].Rank : 0;
        string title = rank > 0 ? string.Create(CultureInfo.InvariantCulture, $"第 {rank} 名") : "对局结束";
        ResultTone tone = rank == 1 ? ResultTone.Win : ResultTone.Neutral;
        int winner = info.WinnerSlot;
        if (winner >= players.Length || !players[winner].Alive) return new ResultsParts(title, "没有人活到最后", tone);
        CountAll(players, out int alive, out _);
        string name = ScoreboardModel.PlayerName(winner < names.Length ? names[winner] : null, winner);
        return new ResultsParts(title, alive > 1 ? $"时间到，{name} 血量领先" : $"{name} 是最后的幸存者", tone);
    }

    /// <summary>"N 秒后返回房间", with the host's shortcut when <paramref name="isHost"/>; the seconds round up.</summary>
    public static string ReturnLine(int ticksLeft, bool isHost)
    {
        string line = string.Create(CultureInfo.InvariantCulture, $"{CeilSeconds(ticksLeft)} 秒后返回房间");
        return isHost ? line + " · Enter 立即返回" : line;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Fraction(int alive, int of) => string.Create(CultureInfo.InvariantCulture, $"{alive}/{of}");

    private static void Count(ReadOnlySpan<PlayerStats> players, byte team, out int alive, out int total)
    {
        alive = total = 0;
        foreach (PlayerStats p in players)
        {
            if (!p.Active || p.Team != team) continue;
            total++;
            if (p.Alive) alive++;
        }
    }

    private static void CountAll(ReadOnlySpan<PlayerStats> players, out int alive, out int total)
    {
        alive = total = 0;
        foreach (PlayerStats p in players)
        {
            if (!p.Active) continue;
            total++;
            if (p.Alive) alive++;
        }
    }
}
