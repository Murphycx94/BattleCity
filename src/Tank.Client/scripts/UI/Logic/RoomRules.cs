using Tank.Net.Battle;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Client.UI;

/// <summary>What starting would seat: the people already in the room plus the bots the server would add, and how they split.</summary>
/// <param name="Team0">Tanks on team 0 (people and bots); zero in a mode without teams.</param>
/// <param name="Team1">Tanks on team 1 (people and bots); zero in a mode without teams.</param>
/// <param name="BotsToTeam0">Of <see cref="Bots"/>, how many the server would put on team 0; zero in a mode without teams.</param>
/// <param name="BotsToTeam1">Of <see cref="Bots"/>, how many the server would put on team 1; zero in a mode without teams.</param>
public readonly record struct StartPlan(int Humans, int Bots, int Team0, int Team1, int BotsToTeam0, int BotsToTeam1)
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
        if (ModeRules.HasBases(room.Mode) && Find(maps ?? MapCatalog.All, room.Map) is { Bases.Count: 0 })   // "the map has no bases"
            return "这张图没有基地，打不了标准战：换一张图，或换个模式。";
        StartPlan plan = PlanFor(room);
        if (plan.Seated < ModeRules.MinPlayers) return "至少要有两辆坦克：等朋友加入，或加几个机器人。";
        if (ModeRules.HasTeams(room.Mode) && (plan.Team0 == 0 || plan.Team1 == 0))
            return "有一队没有坦克：让人换到对面，或加几个机器人。";
        return null;
    }

    private static MapDefinition? Find(IReadOnlyList<MapDefinition> maps, string id)
    {
        foreach (MapDefinition map in maps)
            if (string.Equals(map.Id, id, StringComparison.OrdinalIgnoreCase)) return map;
        return null;
    }

    /// <summary>
    /// Who would be seated if the match started now: every person in the room (a dropped one still holds the seat),
    /// and the bots asked for as far as the room has places, each onto the team with fewer tanks (team 0 on a tie).
    /// </summary>
    public static StartPlan PlanFor(RoomStateMessage room)
    {
        int humans = room.Players.Count(p => !p.IsBot);
        int bots = Math.Min(room.Bots, Math.Max(0, BattleProtocol.MaxPlayers - humans));
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
}
