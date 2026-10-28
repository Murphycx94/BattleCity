using System.Numerics;
using Tank.Client.Minimap;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// One screen of the HUD gallery: the frame, the kills already on the feed, whether the F3 panel is open, whether Tab is
/// "held", and the map's icons over a map from the catalog (null: no map on the screen).
/// </summary>
public sealed record HudDemo(string Name, MatchHudFrame Frame, FeedLine[] Kills, bool NetPanelOpen, bool ScoreboardOpen = false, MinimapDemo? Minimap = null);

/// <summary>The gallery's map: the catalog map <paramref name="MapId"/>'s ground under <paramref name="Frame"/>'s icons, the corner map or the full one.</summary>
public sealed record MinimapDemo(string MapId, MinimapFrame Frame, bool FullOpen);

/// <summary>
/// Canned HUD frames for <c>--hud-demo NAME</c> (blueprint §2.1): every state the match HUD can show, from fixed data, so
/// a screenshot is the same each time and layout problems show without playing a match. The player is slot 0, team 0.
/// Each frame carries a fake scoreboard (players and names); the scoreboard and results screens draw it.
/// </summary>
public static class HudDemoFrames
{
    private const string LongName = "Sergeant_Wolfgang_Becker";   // as long as a name gets on the wire: 24 ASCII bytes
    private static readonly string[] Roster = { "阿成", "小林", "老周", "小雨", "机器人 1", "机器人 2", "机器人 3", "机器人 4", "机器人 5", "机器人 6" };

    private static readonly string[] AllNames = { "timer", "feed", "respawn", "spectate", "spectate-team", "spectate-ffa", "annihilation", "ffa", "net", "scoreboard", "results-win", "results-lose", "results-draw", "results-ffa", "standard", "capture", "minimap", "map-full" };

    /// <summary>The gallery's screen names, in a fixed order.</summary>
    public static IReadOnlyList<string> Names => AllNames;

    public static bool TryGet(string? name, out HudDemo demo)
    {
        demo = name switch
        {
            "timer" => Timer(),
            "feed" => Feed(),
            "respawn" => Respawn(),
            "spectate" => Spectate(),
            "spectate-team" => SpectateTeam(),
            "spectate-ffa" => SpectateFfa(),
            "annihilation" => Annihilation(),
            "ffa" => Solo(),
            "net" => Net(),
            "scoreboard" => Scoreboard(),
            "results-win" => ResultsWin(),
            "results-lose" => ResultsLose(),
            "results-draw" => ResultsDraw(),
            "results-ffa" => ResultsSolo(),
            "standard" => Standard(),
            "capture" => CaptureWin(),
            "minimap" => MapScreen("minimap", fullOpen: false),
            "map-full" => MapScreen("map-full", fullOpen: true),
            _ => null!,
        };
        return demo is not null;
    }

    private static HudDemo Timer() => Make("timer", TeamFrame(Ticks(3, 27), kills0: 4, kills1: 2));

    private static HudDemo Feed()
    {
        MatchHudFrame frame = TeamFrame(Ticks(5, 2), kills0: 7, kills1: 5);
        FeedLine[] kills =
        {
            Line(LongName, Side.Ally, "老周", Side.Enemy),   // a 24-byte name on each side of a row: cut with "…" to keep the feed's width
            Line(null, Side.Enemy, "机器人 2", Side.Enemy),
            Line("阿成", Side.Own, LongName, Side.Ally, friendly: true),
            Line("阿成", Side.Own, "机器人 3", Side.Enemy),
            Line("老周", Side.Enemy, "阿成", Side.Own),
        };
        return new HudDemo("feed", frame, kills, NetPanelOpen: false);
    }

    private static HudDemo Respawn() =>
        Make("respawn", TeamFrame(Ticks(4, 12), kills0: 5, kills1: 6) with { Banner = BannerKind.Respawn, RespawnIn = 6 * SimConstants.TickRate });

    private static HudDemo Spectate()
    {
        PlayerStats[] players = Players(10, i => (byte)(i < 5 ? 0 : 1), i => i is 1 or 2 or 6 or 7, kills: i => i % 4, deaths: i => 1);
        MatchHudFrame frame = Frame(BattleMode.Annihilation, Ticks(2, 36), players, 10) with { Banner = BannerKind.Spectate, SpectateTarget = "机器人 2" };
        return Make("spectate", frame);
    }

    /// <summary>Team battle, the player's wreck waiting to respawn while the camera follows a teammate (blueprint §5 T8): the countdown plate and the spectate bar together, the fatal kill on the feed.</summary>
    private static HudDemo SpectateTeam()
    {
        PlayerStats[] players = Players(6, i => (byte)(i < 3 ? 0 : 1), i => i != 0, kills: i => i + 1, deaths: i => i == 0 ? 2 : i % 3);
        var match = new MatchBlock { TeamScore0 = 6, TeamScore1 = 7, TicksLeft = Ticks(4, 41) };
        MatchHudFrame frame = Frame(BattleMode.TeamBattle, Ticks(4, 41), players, 6) with
        {
            Plate = HudText.Plate(BattleMode.TeamBattle, 0, 0, match, players),
            Banner = BannerKind.Respawn,
            RespawnIn = 7 * SimConstants.TickRate,
            SpectateTarget = Roster[1],
        };
        return new HudDemo("spectate-team", frame, new[] { Line("老周", Side.Enemy, "阿成", Side.Own) }, NetPanelOpen: false);
    }

    /// <summary>Free-for-all, the player dead for good and the camera free (blueprint §5 T8): the bar carries the free camera's hints.</summary>
    private static HudDemo SpectateFfa()
    {
        PlayerStats[] players = Players(8, i => (byte)i, i => i is 1 or 3 or 4 or 6, kills: i => i % 4, deaths: i => i is 1 or 3 or 4 or 6 ? 0 : 1);
        MatchHudFrame frame = Frame(BattleMode.FreeForAll, Ticks(2, 52), players, 8) with
        {
            Banner = BannerKind.Spectate,
            SpectateFree = true,
        };
        return new HudDemo("spectate-ffa", frame, new[] { Line("小雨", Side.Enemy, "阿成", Side.Own) }, NetPanelOpen: false);
    }

    private static HudDemo Annihilation()
    {
        PlayerStats[] players = Players(10, i => (byte)(i < 5 ? 0 : 1), i => i is 0 or 1 or 3 or 7 or 8, kills: i => i % 3, deaths: i => i % 2);
        return Make("annihilation", Frame(BattleMode.Annihilation, Ticks(5, 48), players, 10));
    }

    private static HudDemo Solo()
    {
        PlayerStats[] players = Players(8, i => (byte)i, i => i is 0 or 2 or 5 or 6, kills: i => i == 0 ? 3 : i % 3, deaths: i => i % 2);
        return Make("ffa", Frame(BattleMode.FreeForAll, Ticks(6, 15), players, 8));
    }

    private static HudDemo Net()
    {
        var stats = new BattleStats(38, 3.4, 100, 0f, 7400, 1900, 30, 236, 0, 14, 0.031f, 0.012, 0.43f, 2.1, 1.002, 0);
        MatchHudFrame frame = TeamFrame(Ticks(6, 40), kills0: 2, kills1: 3) with { Net = new NetSample(stats, 0, true, 0.4, 2.1, 144) };
        return new HudDemo("net", frame, Array.Empty<FeedLine>(), NetPanelOpen: true);
    }

    /// <summary>Team battle, eight players on mixed slots: the player (slot 0) leads a team that has one tank down; one of the others is down too.</summary>
    private static HudDemo Scoreboard()
    {
        PlayerStats[] players = Players(8, i => (byte)(i is 0 or 2 or 3 or 6 ? 0 : 1), i => i is not (3 or 5), kills: i => new[] { 4, 1, 2, 0, 3, 1, 2, 5 }[i], deaths: i => new[] { 1, 2, 2, 3, 1, 4, 2, 0 }[i]);
        var match = new MatchBlock { TeamScore0 = 8, TeamScore1 = 9 };
        MatchHudFrame frame = Frame(BattleMode.TeamBattle, Ticks(3, 27), players, 8) with { Plate = HudText.Plate(BattleMode.TeamBattle, 0, 0, match, players) };
        return Make("scoreboard", frame) with { ScoreboardOpen = true };
    }

    private static HudDemo ResultsWin() =>
        Make("results-win", Ended(BattleMode.TeamBattle, Players(6, i => (byte)(i < 3 ? 0 : 1), _ => true, kills: i => 5 - i, deaths: i => i % 3), 6, 12, 9, new ResultsInfo(0, MatchRules.NoSlot, 5 * SimConstants.TickRate, true)));

    private static HudDemo ResultsLose() =>
        Make("results-lose", Ended(BattleMode.Annihilation, Players(8, i => (byte)(i < 4 ? 0 : 1), i => i is 6 or 7, kills: i => i % 3, deaths: i => i < 4 ? 1 : i % 2), 8, 0, 0, new ResultsInfo(1, MatchRules.NoSlot, 3 * SimConstants.TickRate, false)));

    private static HudDemo ResultsDraw() =>
        Make("results-draw", Ended(BattleMode.Annihilation, Players(6, i => (byte)(i < 3 ? 0 : 1), i => i is 1 or 4, kills: i => i % 2, deaths: i => i % 2), 6, 0, 0, new ResultsInfo(MatchRules.NoTeam, MatchRules.NoSlot, 7 * SimConstants.TickRate, false)));

    /// <summary>Free-for-all, six players ranked 1 to 6; the player (slot 0) came third and slot 4 was left standing.</summary>
    private static HudDemo ResultsSolo()
    {
        byte[] ranks = { 3, 2, 5, 6, 1, 4 };
        PlayerStats[] players = Players(6, i => (byte)i, i => i == 4, kills: i => new[] { 2, 3, 1, 0, 4, 2 }[i], deaths: i => i == 4 ? 0 : 1);
        for (int i = 0; i < ranks.Length; i++) players[i].Rank = ranks[i];
        return Make("results-ffa", Ended(BattleMode.FreeForAll, players, 6, 0, 0, new ResultsInfo(MatchRules.NoTeam, 4, 4 * SimConstants.TickRate, false)));
    }

    /// <summary>
    /// Standard battle, eight players, 6:40 left: the own team has 3 of 4 standing, the enemy 2 of 4; the enemy has taken
    /// 37 points of the own base (the red bar; the gallery's clock is frozen at 0, the moment the points rose, so the bar
    /// is drawn in its bright phase) and the own team 62 of theirs. The enemy rows' capture is 0, as the server sends it
    /// during play; the own team's add up to the 62. Two kills on the feed.
    /// </summary>
    private static HudDemo Standard()
    {
        int[] capture = { 24, 21, 0, 17, 0, 0, 0, 0 };
        PlayerStats[] players = Players(8, i => (byte)(i < 4 ? 0 : 1), i => i is 0 or 1 or 3 or 4 or 6, kills: i => new[] { 2, 1, 0, 1, 1, 0, 2, 1 }[i], deaths: i => i is 2 or 5 or 7 ? 1 : 0);
        for (int i = 0; i < capture.Length; i++) players[i].Capture = (byte)capture[i];
        MatchHudFrame frame = Frame(BattleMode.Standard, Ticks(6, 40), players, 8, progress0: 37, progress1: 62);
        FeedLine[] kills =
        {
            Line("阿成", Side.Own, "机器人 4", Side.Enemy),
            Line("机器人 3", Side.Enemy, "老周", Side.Ally),
        };
        return new HudDemo("standard", frame, kills, NetPanelOpen: false);
    }

    /// <summary>
    /// Standard battle won by capture: six players, the own team took the enemy base (its three hold 41, 33 and 26 points
    /// at best), the enemy got 12 into ours. The results show 胜利 · 占领了敌方基地 over a table with the 占领 column.
    /// </summary>
    private static HudDemo CaptureWin()
    {
        int[] capture = { 41, 33, 26, 0, 0, 12 };
        PlayerStats[] players = Players(6, i => (byte)(i < 3 ? 0 : 1), i => i is not (2 or 4), kills: i => new[] { 3, 2, 1, 1, 1, 0 }[i], deaths: i => i is 2 or 4 ? 1 : 0);
        for (int i = 0; i < capture.Length; i++) players[i].Capture = (byte)capture[i];
        var info = new ResultsInfo(0, MatchRules.NoSlot, 6 * SimConstants.TickRate, true, MatchEndReason.Captured);
        return Make("capture", Ended(BattleMode.Standard, players, 6, 0, 0, info, progress0: 12, progress1: BaseCapture.PointsToWin));
    }

    /// <summary>
    /// The standard screen with the riverland map, as the corner map or the full one. The player's arrow stands 120 m out
    /// of the own (west) base towards the enemy's and points at it, the camera turned 0.4 rad to the right of the hull;
    /// both bases carry the standard screen's points (37 taken of ours, 62 of theirs). Four teammates, one of each shape
    /// and one of them a wreck, hold the west; three enemies, one a wreck, are about the middle of the map. Every point is
    /// on the map's lanes and cover points (<see cref="RiverlandMap.Bots"/>), well inside the playable square.
    /// </summary>
    private static HudDemo MapScreen(string name, bool fullOpen) =>
        Standard() with { Name = name, Minimap = new MinimapDemo(RiverlandMap.Id, RiverlandFrame(), fullOpen) };

    private const float EyeOutOfBase = 120f, EyeCameraTurn = 0.4f, EyeHalfFov = 0.9f;

    private static MinimapFrame RiverlandFrame()
    {
        var frame = new MinimapFrame();
        TeamBase own = RiverlandMap.Bases[0], enemy = RiverlandMap.Bases[1];
        frame.AddBase(new MinimapBase(own.Centre, own.Radius, Own: true, Points: 37));
        frame.AddBase(new MinimapBase(enemy.Centre, enemy.Radius, Own: false, Points: 62));

        // Every shape shows standing at least once: the wrecks are a teammate's medium and an enemy's light.
        frame.AddTank(new MinimapTank(new Vector2(330, 450), Side.Ally, MinimapShape.Light, Destroyed: false));
        frame.AddTank(new MinimapTank(new Vector2(330, 530), Side.Ally, MinimapShape.Td, Destroyed: false));
        frame.AddTank(new MinimapTank(new Vector2(300, 620), Side.Ally, MinimapShape.Heavy, Destroyed: false));
        frame.AddTank(new MinimapTank(new Vector2(214, 430), Side.Ally, MinimapShape.Medium, Destroyed: true));
        frame.AddTank(new MinimapTank(new Vector2(560, 410), Side.Enemy, MinimapShape.Medium, Destroyed: false));
        frame.AddTank(new MinimapTank(new Vector2(670, 556), Side.Enemy, MinimapShape.Heavy, Destroyed: false));
        frame.AddTank(new MinimapTank(new Vector2(564, 624), Side.Enemy, MinimapShape.Light, Destroyed: true));

        Vector2 towards = Vector2.Normalize(enemy.Centre - own.Centre);
        float yaw = Angles.YawOf(towards);
        frame.Eye = new MinimapEye(EyeKind.Arrow, own.Centre + towards * EyeOutOfBase, yaw, Angles.Wrap(yaw - EyeCameraTurn), EyeHalfFov);
        return frame;
    }

    /// <summary>The match over: the plate and the results banner for <paramref name="info"/>, the clock at 0.</summary>
    private static MatchHudFrame Ended(BattleMode mode, PlayerStats[] players, int count, int score0, int score1, ResultsInfo info, byte progress0 = 0, byte progress1 = 0)
    {
        var match = new MatchBlock { Phase = MatchPhase.Ended, TeamScore0 = score0, TeamScore1 = score1 };
        MatchHudFrame frame = Frame(mode, ticksLeft: 0, players, count, progress0, progress1);
        return frame with { Phase = MatchPhase.Ended, Plate = HudText.Plate(mode, 0, 0, match, players), Banner = BannerKind.Results, Results = info };
    }

    private static HudDemo Make(string name, MatchHudFrame frame) => new(name, frame, Array.Empty<FeedLine>(), NetPanelOpen: false);

    private static MatchHudFrame TeamFrame(int ticksLeft, int kills0, int kills1)
    {
        PlayerStats[] players = Players(6, i => (byte)(i < 3 ? 0 : 1), _ => true, kills: i => i + 1, deaths: i => i % 3);
        MatchHudFrame frame = Frame(BattleMode.TeamBattle, ticksLeft, players, 6);
        var match = new MatchBlock { TeamScore0 = kills0, TeamScore1 = kills1, TicksLeft = ticksLeft };
        return frame with { Plate = HudText.Plate(BattleMode.TeamBattle, 0, 0, match, players) };
    }

    /// <summary>A playing frame for the player in slot 0, team 0; <paramref name="progress0"/> and <paramref name="progress1"/> are the bases' points (standard only), which give the capture bars as the scene's frame would.</summary>
    private static MatchHudFrame Frame(BattleMode mode, int ticksLeft, PlayerStats[] players, int count, byte progress0 = 0, byte progress1 = 0)
    {
        var match = new MatchBlock { TicksLeft = ticksLeft, BaseProgress0 = progress0, BaseProgress1 = progress1 };
        PlateValues plate = HudText.Plate(mode, 0, 0, match, players);
        var names = new string?[BattleProtocol.MaxPlayers];
        Array.Copy(Roster, names, count);
        var net = new NetSample(default, 0, false, 0, 0, 60);
        return new MatchHudFrame(mode, MatchPhase.Playing, ticksLeft, 0, 0, plate, BannerKind.None, 0, null, false, net, players, names, ResultsInfo.None,
            CaptureText.Bars(mode, 0, match));
    }

    private static PlayerStats[] Players(int count, Func<int, byte> team, Func<int, bool> alive, Func<int, int> kills, Func<int, int> deaths)
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        for (int i = 0; i < count; i++)
            players[i] = new PlayerStats { Active = true, Team = team(i), TankId = i + 1, Spec = (byte)(i % 4), Kills = kills(i), Deaths = deaths(i), Alive = alive(i) };
        return players;
    }

    private static FeedLine Line(string? killer, Side killerSide, string victim, Side victimSide, bool friendly = false) =>
        new(killer, killerSide, victim, victimSide, friendly, ExpiresAt: 0);

    private static int Ticks(int minutes, int seconds) => (minutes * 60 + seconds) * SimConstants.TickRate;
}
