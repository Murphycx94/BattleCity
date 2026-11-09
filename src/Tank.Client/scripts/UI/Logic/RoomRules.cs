using Tank.Net.Battle;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Client.UI;

/// <summary>What starting would seat: the people already in the room plus the bots the server would add, and how they split.</summary>
/// <param name="Team0">Tanks on team 0 (people and bots); zero in a mode without teams.</param>
/// <param name="Team1">Tanks on team 1 (people and bots); zero in a mode without teams.</param>
/// <param name="BotsToTeam0">Of <see cref="Bots"/>, how many the server would put on team 0; zero in a mode without teams.</param>
/// <param name="BotsToTeam1">Of <see cref="Bots"/>, how many the server would put on team 1; zero in a mode without teams.</param>
/// <param name="TeamsManned">Duos: the teams that would have a tank (people's teams and those <see cref="RoomRules.DuoBots"/> fills); zero in every other mode.</param>
public readonly record struct StartPlan(int Humans, int Bots, int Team0, int Team1, int BotsToTeam0, int BotsToTeam1, int TeamsManned = 0)
{
    public int Seated => Humans + Bots;
}

/// <summary>
/// Whether the host's Start button can work: the same checks as <c>BattleServer.TryStartMatch</c>, worked out from the room
/// state the client sees, so the button can say why not before the server has to refuse. The cross-check test runs both.
/// </summary>
public static class RoomRules
{
    /// <summary>Why the match can't start now, in words for the host; null when it can.</summary>
    /// <param name="maps">
    /// The maps the room's map is looked up in (by id, ignoring case, as the server does): <see cref="MapCatalog.All"/>
    /// when null. A map not among them is left to the server: the client can't tell whether it has bases.
    /// </param>
    public static string? StartBlockReason(RoomStateMessage room, IReadOnlyList<MapDefinition>? maps = null)
    {
        if (room.MatchRunning) return "对局进行中，等这一局结束。";
        switch (room.MapState)   // the server's "the map is still being generated" and "the map failed to generate"
        {
            case MapState.Generating: return "地图生成中…";
            case MapState.Failed: return "地图生成失败，换一张图。";
        }
        if (!ModeRules.AllowsMap(room.Mode, room.Map)) return NotHereReason;   // "royale is played on the riverland only"
        if (ModeRules.HasBases(room.Mode) && Find(maps ?? MapCatalog.All, room.Map) is { Bases.Count: 0 })   // "the map has no bases"
            return "这张图没有基地，打不了标准战：换一张图，或换个模式。";
        StartPlan plan = PlanFor(room);
        if (plan.Seated < ModeRules.MinPlayers) return "至少要有两辆坦克：等朋友加入，或加几个机器人。";
        if (ModeRules.HasTeams(room.Mode) && (plan.Team0 == 0 || plan.Team1 == 0))
            return "有一队没有坦克：让人换到对面，或加几个机器人。";
        if (ModeRules.IsDuos(room.Mode) && plan.TeamsManned < 2) return DuoTeamsReason;   // the server's "fewer than two teams"
        return null;
    }

    /// <summary>Duos with tanks on one team only.</summary>
    public const string DuoTeamsReason = "至少要有两队有车：让朋友选别的队，或加几个机器人。";

    /// <summary>The one reason a mode bound to a map gives (the royale and the riverland are the only pair).</summary>
    public const string NotHereReason = "吃鸡只在河谷开：换到河谷。";

    private static MapDefinition? Find(IReadOnlyList<MapDefinition> maps, string id)
    {
        foreach (MapDefinition map in maps)
            if (string.Equals(map.Id, id, StringComparison.OrdinalIgnoreCase)) return map;
        return null;
    }

    /// <summary>
    /// Who would be seated if the match started now: every person in the room (a dropped one still holds the seat),
    /// and the bots asked for as far as the room has places — in a two-team mode each onto the team with fewer tanks
    /// (team 0 on a tie), in duos as <see cref="DuoBots"/> has them.
    /// </summary>
    public static StartPlan PlanFor(RoomStateMessage room)
    {
        int humans = room.Players.Count(p => !p.IsBot);
        int bots = BotsFor(room, humans);
        if (ModeRules.IsDuos(room.Mode))
        {
            Span<int> counts = stackalloc int[ModeRules.DuoTeams];
            SeatDuos(room, bots, counts);
            return new StartPlan(humans, bots, 0, 0, 0, 0, DuoSeating.Manned(counts));
        }
        if (!ModeRules.HasTeams(room.Mode)) return new StartPlan(humans, bots, 0, 0, 0, 0);

        int team0 = room.Players.Count(p => !p.IsBot && p.Team == 0);
        int team1 = humans - team0;
        int botsToTeam0 = 0;
        for (int i = 0; i < bots; i++)
        {
            if (team1 < team0) team1++;
            else
            {
                team0++;
                botsToTeam0++;
            }
        }
        return new StartPlan(humans, bots, team0, team1, botsToTeam0, bots - botsToTeam0);
    }

    /// <summary>
    /// Duos: how many bots the server would add to each team at the start (length <see cref="ModeRules.DuoTeams"/>): the bots
    /// asked for as far as the room has places, one at a time by <see cref="DuoSeating.ForBot"/> on top of the people's teams,
    /// as <c>BattleServer.TryStartMatch</c> seats them. All zero in every other mode.
    /// </summary>
    public static int[] DuoBots(RoomStateMessage room)
    {
        var added = new int[ModeRules.DuoTeams];
        if (!ModeRules.IsDuos(room.Mode)) return added;
        Span<int> counts = stackalloc int[ModeRules.DuoTeams];
        int bots = BotsFor(room, room.Players.Count(p => !p.IsBot));
        SeatDuos(room, bots, counts, added);
        return added;
    }

    /// <summary>The bots asked for as far as the mode has places beside the people.</summary>
    private static int BotsFor(RoomStateMessage room, int humans) =>
        Math.Min(room.Bots, Math.Max(0, ModeRules.MaxPlayersFor(room.Mode) - humans));

    /// <summary>
    /// Fills <paramref name="counts"/> with the people on each duos team (a seat on no team 0–7 isn't counted, as the server
    /// counts), then seats <paramref name="bots"/> by <see cref="DuoSeating.ForBot"/>, noting each in <paramref name="added"/> when given.
    /// </summary>
    private static void SeatDuos(RoomStateMessage room, int bots, Span<int> counts, int[]? added = null)
    {
        counts.Clear();
        foreach (RoomPlayer p in room.Players)
            if (!p.IsBot && p.Team < counts.Length) counts[p.Team]++;
        for (int i = 0; i < bots; i++)
        {
            byte team = DuoSeating.ForBot(counts);
            if (team >= counts.Length) break;   // every team full: no place (16 seats at most, so never in a real room)
            counts[team]++;
            if (added is not null) added[team]++;
        }
    }
}
