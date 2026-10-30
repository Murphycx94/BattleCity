using System.Globalization;
using Tank.Client.UI;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// One line of the scoreboard. <c>Rank</c> is 1-based: the place in the player's own team in the team modes, the place
/// in the field in a free-for-all (the server's own after the match, the standing while it runs). <c>Side</c> is the
/// line's colour relative to the player; <c>Status</c> is the words of the last column. <c>Capture</c> is the most
/// base points the player's tank held this match (<see cref="PlayerStats.Capture"/>; standard only, and the server
/// sends 0 for enemies until the match has ended — see <see cref="ScoreboardModel.CaptureCell"/>). <c>Damage</c> is the
/// damage dealt to other sides' tanks and <c>Lasted</c> how long the tank lasted, in whole seconds (both shown in a
/// royale; the server sends another player's damage as 0 until the end — <see cref="ScoreboardModel.DamageCell"/>).
/// </summary>
public readonly record struct ScoreRow(byte Slot, string Name, byte Team, string TankName, int Kills, int Deaths, bool Alive, int Rank, Side Side, string Status,
    int Capture = 0, int Damage = 0, int Lasted = 0);

/// <summary>
/// The scoreboard's lines and their order (blueprint §5 T7), none of it touching the engine. Team modes: the player's
/// team first, each team by kills (most first), then deaths (fewest first), then slot. Free-for-all and royale: while
/// the match runs the living first, then kills and deaths as above; once it has ended the server's <c>Rank</c>. Players
/// who have left are not listed.
/// </summary>
public static class ScoreboardModel
{
    public const string AliveText = "存活", DestroyedText = "已毁";

    private static readonly string[] SlotNames = BuildSlotNames();
    private static readonly string[] CaptureNumbers = BuildCaptureNumbers();

    /// <summary>The name shown for <paramref name="slot"/>: <paramref name="name"/>, or "玩家 N" (1-based) when the room has none.</summary>
    public static string PlayerName(string? name, int slot)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name;
        return (uint)slot < (uint)SlotNames.Length ? SlotNames[slot] : string.Create(CultureInfo.InvariantCulture, $"玩家 {slot + 1}");
    }

    /// <summary>"复活中 N 秒": the own row's status while the tank is a wreck that is coming back, rounded up so it never reads 0.</summary>
    public static string RespawnStatus(int respawnIn) => string.Create(CultureInfo.InvariantCulture, $"复活中 {HudText.CeilSeconds(respawnIn)} 秒");

    /// <summary>
    /// The 占领 cell of <paramref name="row"/>: the points as a number, except an enemy's while the match is on, which is
    /// left blank. The server hides an enemy's capture until the end (it sends 0), and a 0 there would read as "never
    /// captured". Allocates nothing for 0–100.
    /// </summary>
    public static string CaptureCell(in ScoreRow row, bool ended)
    {
        if (row.Side == Side.Enemy && !ended) return string.Empty;
        return (uint)row.Capture < (uint)CaptureNumbers.Length ? CaptureNumbers[row.Capture] : row.Capture.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The 伤害 cell of <paramref name="row"/>: the number, except another player's while the match is on (the server sends
    /// 0 until the end, plan deviation 6), which is left blank. Allocates: a view calls it when it rebuilds its rows.
    /// </summary>
    public static string DamageCell(in ScoreRow row, bool ended)
    {
        if (row.Side != Side.Own && !ended) return string.Empty;
        return row.Damage.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The 存活 cell once the match has ended: "m:ss" of <see cref="ScoreRow.Lasted"/>. Allocates, as <see cref="DamageCell"/>.</summary>
    public static string LastedCell(in ScoreRow row) => HudText.Clock(row.Lasted);

    /// <summary>
    /// Fills <paramref name="output"/> with the lines in display order and returns how many. Allocates nothing unless the
    /// own row's respawn status needs a new string; <paramref name="output"/> needs room for every slot of
    /// <paramref name="players"/>. <paramref name="ownRespawnIn"/> is the ticks until the own tank is back (0 when it isn't waiting).
    /// </summary>
    public static int Build(BattleMode mode, byte ownSlot, byte ownTeam, ReadOnlySpan<PlayerStats> players, Func<byte, string?> nameOf,
        bool ended, int ownRespawnIn, Span<ScoreRow> output)
    {
        if (output.Length < players.Length) throw new ArgumentException("The output cannot hold every slot.", nameof(output));
        bool teams = ModeRules.HasTeams(mode);
        bool byRank = !teams && ended;
        int count = 0;
        for (int slot = 0; slot < players.Length; slot++)
        {
            PlayerStats p = players[slot];
            if (!p.Active) continue;
            bool own = slot == ownSlot;
            string status = p.Alive ? AliveText : own && ownRespawnIn > 0 ? RespawnStatus(ownRespawnIn) : DestroyedText;
            output[count++] = new ScoreRow((byte)slot, PlayerName(nameOf((byte)slot), slot), p.Team, ModeText.TankName(p.Spec),
                p.Kills, p.Deaths, p.Alive, p.Rank, MatchColors.SideOf(mode, ownTeam, p.Team, own), status, p.Capture, p.Damage, p.Lasted);
        }
        Span<ScoreRow> rows = output[..count];
        for (int i = 1; i < count; i++)
        {
            ScoreRow key = rows[i];
            int j = i - 1;
            while (j >= 0 && Before(key, rows[j], teams, byRank, ownTeam)) { rows[j + 1] = rows[j]; j--; }
            rows[j + 1] = key;
        }
        Number(rows, teams, byRank);
        return count;
    }

    /// <summary>The same as a list, for callers that don't mind the allocation (tests).</summary>
    public static IReadOnlyList<ScoreRow> Build(BattleMode mode, byte ownSlot, byte ownTeam, ReadOnlySpan<PlayerStats> players, Func<byte, string?> nameOf, bool ended, int ownRespawnIn)
    {
        var buffer = new ScoreRow[players.Length];
        int count = Build(mode, ownSlot, ownTeam, players, nameOf, ended, ownRespawnIn, buffer);
        return buffer[..count];
    }

    private static bool Before(in ScoreRow a, in ScoreRow b, bool teams, bool byRank, byte ownTeam)
    {
        if (teams && a.Team != b.Team) return a.Team == ownTeam;
        if (byRank && a.Rank != b.Rank) return RankKey(a) < RankKey(b);
        if (!teams && !byRank && a.Alive != b.Alive) return a.Alive;
        if (!byRank && a.Kills != b.Kills) return a.Kills > b.Kills;
        if (!byRank && a.Deaths != b.Deaths) return a.Deaths < b.Deaths;
        return a.Slot < b.Slot;
    }

    /// <summary>A server rank of 0 (never ranked) sorts last.</summary>
    private static int RankKey(in ScoreRow row) => row.Rank == 0 ? int.MaxValue : row.Rank;

    /// <summary>Sets <c>Rank</c> to the place within the team, or within the field; the server's rank stands after a free-for-all.</summary>
    private static void Number(Span<ScoreRow> rows, bool teams, bool byRank)
    {
        int place = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            place = teams && i > 0 && rows[i].Team == rows[i - 1].Team ? place + 1 : teams ? 1 : i + 1;
            if (byRank && rows[i].Rank > 0) continue;
            rows[i] = rows[i] with { Rank = place };
        }
    }

    private static string[] BuildCaptureNumbers()
    {
        var numbers = new string[BaseCapture.PointsToWin + 1];
        for (int i = 0; i < numbers.Length; i++) numbers[i] = i.ToString(CultureInfo.InvariantCulture);
        return numbers;
    }

    private static string[] BuildSlotNames()
    {
        var names = new string[BattleProtocol.MaxPlayers];
        for (int i = 0; i < names.Length; i++) names[i] = string.Create(CultureInfo.InvariantCulture, $"玩家 {i + 1}");
        return names;
    }
}
