using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>What the centre banner shows.</summary>
public enum BannerKind : byte { None, Respawn, Spectate, Results }

/// <summary>
/// What the results screen needs besides the scoreboard: the server's verdict and why the match ended, the ticks left
/// before the server sends everyone back to the room (counted by the scene from the moment it saw the match end) and
/// whether the player may send them early.
/// </summary>
public readonly record struct ResultsInfo(byte WinnerTeam, byte WinnerSlot, int TicksLeft, bool IsHost,
    MatchEndReason Reason = MatchEndReason.None)
{
    /// <summary>No verdict: the match is on.</summary>
    public static readonly ResultsInfo None = new(MatchRules.NoTeam, MatchRules.NoSlot, 0, false);
}

/// <summary>
/// Everything the match HUD needs for one frame (blueprint §2.1): a plain value the scene fills from the client and the
/// gallery from canned data. <c>Players</c> (indexed by room slot) and <c>Names</c> are the caller's arrays, handed on by
/// reference for the scoreboard; nothing here is copied per frame. <c>Capture</c> is the bars under the plate (none
/// outside a standard match).
/// </summary>
public readonly record struct MatchHudFrame(
    BattleMode Mode,
    MatchPhase Phase,
    int TicksLeft,
    byte OwnSlot,
    byte OwnTeam,
    PlateValues Plate,
    BannerKind Banner,
    int RespawnIn,
    string? SpectateTarget,
    bool SpectateFree,
    NetSample Net,
    PlayerStats[] Players,
    string?[] Names,
    ResultsInfo Results,
    CaptureBars Capture = default)
{
    /// <summary>The slot that has <paramref name="tankId"/> on the scoreboard, or -1 for a tank nobody plays (or has left).</summary>
    public static int SlotOf(PlayerStats[] players, int tankId)
    {
        if (tankId == 0) return -1;
        for (int i = 0; i < players.Length; i++)
            if (players[i].Active && players[i].TankId == tankId) return i;
        return -1;
    }

    /// <summary>
    /// The frame from the client's newest snapshot. The results banner outranks everything; the respawn banner shows while
    /// the own tank is a wreck that is coming back (team battle only), and then <paramref name="spectateTarget"/> or
    /// <paramref name="spectateFree"/> still turn the bottom bar on under it. <paramref name="names"/> is the
    /// room's names by slot, <paramref name="fps"/> the engine's frame rate, <paramref name="resultsLeft"/> the ticks left on the results screen. Reads only; allocates nothing.
    /// </summary>
    public static MatchHudFrame From(BattleClient client, BattleSession? session, byte ownTeam, string?[] names, int fps, string? spectateTarget = null, bool spectateFree = false, int resultsLeft = 0)
    {
        BattleMode mode = client.MatchInfo?.Mode ?? client.Room?.Mode ?? BattleMode.TeamBattle;
        MatchBlock match = client.Match;
        PlayerStats[] players = client.Latest?.Players ?? NoPlayers;
        byte slot = client.Slot;
        var plate = HudText.Plate(mode, slot, ownTeam, match, players);
        BannerKind banner = BannerKind.None;
        if (match.Phase == MatchPhase.Ended) banner = BannerKind.Results;
        else if (match.RespawnIn > 0 && ModeRules.Respawns(mode) && match.Phase == MatchPhase.Playing) banner = BannerKind.Respawn;
        else if (spectateTarget is not null || spectateFree) banner = BannerKind.Spectate;
        (double avg, double max) = session?.ServerTick ?? default;
        var net = new NetSample(client.Stats, client.Resyncs, session?.ServerTick is not null, avg, max, fps);
        return new MatchHudFrame(mode, match.Phase, match.TicksLeft, slot, ownTeam, plate, banner, match.RespawnIn, spectateTarget, spectateFree, net, players, names,
            new ResultsInfo(match.WinnerTeam, match.WinnerSlot, resultsLeft, client.IsHost, match.EndReason),
            CaptureText.Bars(mode, ownTeam, match));
    }

    private static readonly PlayerStats[] NoPlayers = new PlayerStats[BattleProtocol.MaxPlayers];
}
