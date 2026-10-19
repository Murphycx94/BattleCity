namespace Tank.Sim.Modes;

/// <summary>The three ways to fight a match (stage 4 design §2.1).</summary>
public enum BattleMode : byte
{
    /// <summary>Two teams; a knocked-out tank respawns after ten seconds; kills score; the higher score at seven minutes wins.</summary>
    TeamBattle = 0,
    /// <summary>Two teams, no respawns; wiping the other team out wins; a draw at seven minutes.</summary>
    Annihilation = 1,
    /// <summary>Every tank for itself, no respawns; the last one left wins, the rest rank by when they fell; health decides at ten minutes.</summary>
    FreeForAll = 2,
}

/// <summary>The rules that go with each <see cref="BattleMode"/>, and the numbers shared by all three (design §2.2).</summary>
public static class ModeRules
{
    public const int MaxPlayers = 10;
    public const int MinPlayers = 2;
    public const int ProtectionTicks = 5 * SimConstants.TickRate;
    public const int RespawnTicks = 10 * SimConstants.TickRate;
    public const int ResultsTicks = 8 * SimConstants.TickRate;
    private const int TeamMinutes = 7, SoloMinutes = 10;

    public static readonly IReadOnlyList<BattleMode> All = new[] { BattleMode.TeamBattle, BattleMode.Annihilation, BattleMode.FreeForAll };

    public static bool HasTeams(BattleMode mode) => mode != BattleMode.FreeForAll;

    public static bool Respawns(BattleMode mode) => mode == BattleMode.TeamBattle;

    /// <summary>Whether a player may join a match already under way (otherwise they wait in the room for the next).</summary>
    public static bool JoinsMidMatch(BattleMode mode) => mode == BattleMode.TeamBattle;

    public static int DurationTicks(BattleMode mode) => (mode == BattleMode.FreeForAll ? SoloMinutes : TeamMinutes) * 60 * SimConstants.TickRate;

    /// <summary>The name on the command line and in room messages.</summary>
    public static string Id(BattleMode mode) => mode switch
    {
        BattleMode.TeamBattle => "teambattle",
        BattleMode.Annihilation => "annihilation",
        BattleMode.FreeForAll => "ffa",
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
