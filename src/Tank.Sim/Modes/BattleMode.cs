using Tank.Sim.Maps;

namespace Tank.Sim.Modes;

/// <summary>The five ways to fight a match (stage 4 design §2.1, stage 5 design §3.1, stage 6 design §2). Values on the wire: append only.</summary>
public enum BattleMode : byte
{
    /// <summary>Two teams; a knocked-out tank respawns after ten seconds; kills score; the higher score at seven minutes wins.</summary>
    TeamBattle = 0,
    /// <summary>Two teams, no respawns; wiping the other team out wins; a draw at seven minutes.</summary>
    Annihilation = 1,
    /// <summary>Every tank for itself, no respawns; the last one left wins, the rest rank by when they fell; health decides at ten minutes.</summary>
    FreeForAll = 2,
    /// <summary>Two teams, a base each, no respawns; wiping the other team out or taking its base wins; a draw at ten minutes.</summary>
    Standard = 3,
    /// <summary>Every tank for itself, up to 16, no respawns, on the riverland only; the zone closes in five rings; the last one left wins (stage 6 design §2, §3).</summary>
    Royale = 4,
}

/// <summary>The rules that go with each <see cref="BattleMode"/>, and the numbers shared by all of them (design §2.2).</summary>
public static class ModeRules
{
    /// <summary>The most seats any mode has: what the arrays, the server's seats and the wire are sized by. Per mode: <see cref="MaxPlayersFor"/>.</summary>
    public const int MaxPlayers = 16;

    /// <summary>The seats of a team battle, an annihilation, a free-for-all and a standard battle.</summary>
    public const int DefaultMaxPlayers = 10;

    public const int RoyaleMaxPlayers = 16;
    public const int MinPlayers = 2;
    public const int ProtectionTicks = 5 * SimConstants.TickRate;
    public const int RespawnTicks = 10 * SimConstants.TickRate;
    public const int ResultsTicks = 8 * SimConstants.TickRate;
    private const int TeamMinutes = 7, SoloMinutes = 10, StandardMinutes = 10;

    /// <summary>The highest mode value: what the wire readers accept.</summary>
    public const BattleMode LastMode = BattleMode.Royale;

    /// <summary>Every mode, in the order the room lists them.</summary>
    public static readonly IReadOnlyList<BattleMode> All = new[] { BattleMode.TeamBattle, BattleMode.Annihilation, BattleMode.FreeForAll, BattleMode.Standard, BattleMode.Royale };

    /// <summary>The seats <paramref name="mode"/> has room for, bots included.</summary>
    public static int MaxPlayersFor(BattleMode mode) => mode == BattleMode.Royale ? RoyaleMaxPlayers : DefaultMaxPlayers;

    /// <summary>The most bots a room in <paramref name="mode"/> may ask for: every seat but one (the host's).</summary>
    public static int MaxBotsFor(BattleMode mode) => MaxPlayersFor(mode) - 1;

    public static bool HasTeams(BattleMode mode) => mode is not (BattleMode.FreeForAll or BattleMode.Royale);

    public static bool Respawns(BattleMode mode) => mode == BattleMode.TeamBattle;

    /// <summary>Whether a player may join a match already under way (otherwise they wait in the room for the next).</summary>
    public static bool JoinsMidMatch(BattleMode mode) => mode is BattleMode.TeamBattle or BattleMode.Standard;

    /// <summary>Whether each team has a base the other can capture (the map must have them).</summary>
    public static bool HasBases(BattleMode mode) => mode == BattleMode.Standard;

    /// <summary>Whether the match has a zone closing in on it (stage 6 design §3).</summary>
    public static bool HasZone(BattleMode mode) => mode == BattleMode.Royale;

    /// <summary>Whether supply crates lie about the map (stage 6 design §4): the modes with a zone (a royale; 6C's duos).</summary>
    public static bool HasCrates(BattleMode mode) => HasZone(mode);

    /// <summary>The one map the mode is played on (a <see cref="MapCatalog"/> id), or null for any: royale → <see cref="RiverlandMap.Id"/>.</summary>
    public static string? OnlyMap(BattleMode mode) => mode == BattleMode.Royale ? RiverlandMap.Id : null;

    /// <summary>Whether <paramref name="mapId"/> (ignoring case) may host <paramref name="mode"/>.</summary>
    public static bool AllowsMap(BattleMode mode, string mapId) =>
        OnlyMap(mode) is not { } only || string.Equals(only, mapId, StringComparison.OrdinalIgnoreCase);

    public static int DurationTicks(BattleMode mode) => mode switch
    {
        BattleMode.FreeForAll or BattleMode.Royale => SoloMinutes,
        BattleMode.Standard => StandardMinutes,
        _ => TeamMinutes,
    } * 60 * SimConstants.TickRate;

    /// <summary>The name on the command line and in room messages.</summary>
    public static string Id(BattleMode mode) => mode switch
    {
        BattleMode.TeamBattle => "teambattle",
        BattleMode.Annihilation => "annihilation",
        BattleMode.FreeForAll => "ffa",
        BattleMode.Standard => "standard",
        BattleMode.Royale => "royale",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode."),
    };

    public static bool TryParse(string? id, out BattleMode mode)
    {
        foreach (BattleMode m in All)
            if (string.Equals(Id(m), id, StringComparison.OrdinalIgnoreCase)) { mode = m; return true; }
        mode = default;
        return false;
    }
}
