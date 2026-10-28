using System.Numerics;
using Tank.Client.Match;
using Tank.Client.Minimap;
using Tank.Net.Battle;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class HudDemoFramesTests
{
    public static TheoryData<string> Demos
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string name in HudDemoFrames.Names) data.Add(name);
            return data;
        }
    }

    [Fact]
    public void The_gallery_has_the_screens_the_blueprint_names()
    {
        foreach (string name in new[] { "timer", "feed", "respawn", "spectate", "spectate-team", "spectate-ffa", "annihilation", "ffa", "scoreboard", "results-win", "results-lose", "results-draw", "results-ffa", "standard", "capture" })
            Assert.Contains(name, HudDemoFrames.Names);
    }

    [Fact]
    public void An_unknown_name_gives_no_demo()
    {
        Assert.False(HudDemoFrames.TryGet("nope", out _));
        Assert.False(HudDemoFrames.TryGet(null, out _));
    }

    [Theory]
    [MemberData(nameof(Demos))]
    public void Every_demo_frame_is_self_consistent(string name)
    {
        Assert.True(HudDemoFrames.TryGet(name, out HudDemo demo));
        MatchHudFrame frame = demo.Frame;

        Assert.Equal(name, demo.Name);
        Assert.Equal(BattleProtocol.MaxPlayers, frame.Players.Length);
        Assert.Equal(frame.Players.Length, frame.Names.Length);
        int active = frame.Players.Count(p => p.Active);
        Assert.InRange(active, 2, ModeRules.MaxPlayers);
        for (int slot = 0; slot < frame.Players.Length; slot++)
        {
            PlayerStats p = frame.Players[slot];
            if (!p.Active) { Assert.True(string.IsNullOrEmpty(frame.Names[slot])); continue; }
            Assert.False(string.IsNullOrWhiteSpace(frame.Names[slot]), $"slot {slot} has no name");
            Assert.True(p.Kills >= 0 && p.Deaths >= 0);
            if (frame.Phase == MatchPhase.Playing) Assert.Equal(0, p.Rank);   // nobody is ranked while the match runs
            Assert.True(p.TankId > 0);
            if (ModeRules.HasTeams(frame.Mode)) Assert.InRange(p.Team, 0, 1);
        }
        Assert.Equal(active, frame.Players.Where(p => p.Active).Select(p => p.TankId).Distinct().Count());
        Assert.True(frame.Players[frame.OwnSlot].Active);
        Assert.True(frame.Phase == MatchPhase.Ended || frame.TicksLeft > 0);
        Assert.True(frame.TicksLeft <= ModeRules.DurationTicks(frame.Mode));
        Assert.Equal(frame.Phase == MatchPhase.Ended, frame.Banner == BannerKind.Results);
        if (frame.Mode != BattleMode.Standard) Assert.Equal(CaptureBars.None, frame.Capture);   // the bars are a standard match's alone
        Assert.Equal(1, ScoreboardRows(frame).Count(r => r.Side == Side.Own));
        Assert.True(demo.Kills.Length <= KillFeed.Capacity);
        foreach (FeedLine line in demo.Kills)
        {
            Assert.False(string.IsNullOrWhiteSpace(line.Victim));
            Assert.False(string.IsNullOrWhiteSpace(line.Text));
            Assert.True(line.Killer is null || line.Killer.Length > 0);
        }
    }

    private static IReadOnlyList<ScoreRow> ScoreboardRows(MatchHudFrame f) =>
        ScoreboardModel.Build(f.Mode, f.OwnSlot, f.OwnTeam, f.Players, slot => f.Names[slot], f.Phase == MatchPhase.Ended, f.RespawnIn);

    [Theory]
    [MemberData(nameof(Demos))]
    public void The_plate_in_every_demo_matches_its_players(string name)
    {
        HudDemoFrames.TryGet(name, out HudDemo demo);
        MatchHudFrame f = demo.Frame;
        var match = new MatchBlock { TeamScore0 = f.Plate.Kind == PlateKind.TeamKills ? f.Plate.Left : 0, TeamScore1 = f.Plate.Kind == PlateKind.TeamKills ? f.Plate.Right : 0 };
        Assert.Equal(f.Plate, HudText.Plate(f.Mode, f.OwnSlot, f.OwnTeam, match, f.Players));
    }

    [Fact]
    public void The_named_screens_show_what_they_are_named_for()
    {
        HudDemoFrames.TryGet("timer", out HudDemo timer);
        Assert.Equal("3:27", HudText.Timer(timer.Frame.TicksLeft));
        Assert.Equal(new PlateValues(PlateKind.TeamKills, 4, 0, 2, 0), timer.Frame.Plate);

        HudDemoFrames.TryGet("feed", out HudDemo feed);
        Assert.Equal(KillFeed.Capacity, feed.Kills.Length);
        Assert.Contains(feed.Kills, l => l.Killer is null);
        Assert.Contains(feed.Kills, l => l.Friendly);

        HudDemoFrames.TryGet("respawn", out HudDemo respawn);
        Assert.Equal(BannerKind.Respawn, respawn.Frame.Banner);
        Assert.Equal("6 秒后复活", HudText.RespawnLine(respawn.Frame.RespawnIn));

        HudDemoFrames.TryGet("spectate", out HudDemo spectate);
        Assert.Equal("观战 机器人 2 · Q/E 切换", HudText.SpectateLine(spectate.Frame.SpectateTarget!));

        // spectate-team: the wreck waits for the respawn while the camera follows a teammate — the countdown and the bar together.
        HudDemoFrames.TryGet("spectate-team", out HudDemo spectateTeam);
        Assert.Equal(BattleMode.TeamBattle, spectateTeam.Frame.Mode);
        Assert.Equal(BannerKind.Respawn, spectateTeam.Frame.Banner);
        Assert.True(spectateTeam.Frame.RespawnIn > 0);
        Assert.False(spectateTeam.Frame.Players[0].Alive);
        int targetSlot = Array.IndexOf(spectateTeam.Frame.Names, spectateTeam.Frame.SpectateTarget);
        Assert.True(targetSlot >= 0 && spectateTeam.Frame.Players[targetSlot] is { Team: 0, Alive: true }, "the watched tank is a living teammate");

        // spectate-ffa: dead for good, the camera free.
        HudDemoFrames.TryGet("spectate-ffa", out HudDemo spectateFfa);
        Assert.Equal(BattleMode.FreeForAll, spectateFfa.Frame.Mode);
        Assert.Equal(BannerKind.Spectate, spectateFfa.Frame.Banner);
        Assert.True(spectateFfa.Frame.SpectateFree);
        Assert.Null(spectateFfa.Frame.SpectateTarget);
        Assert.False(spectateFfa.Frame.Players[0].Alive);
        Assert.Equal(0, spectateFfa.Frame.RespawnIn);

        HudDemoFrames.TryGet("annihilation", out HudDemo annihilation);
        Assert.Equal(("3/5", "2/5"), (HudText.Parts(annihilation.Frame.Plate).LeftValue, HudText.Parts(annihilation.Frame.Plate).RightValue));

        HudDemoFrames.TryGet("ffa", out HudDemo ffa);
        Assert.Equal(("3", "4/8"), (HudText.Parts(ffa.Frame.Plate).LeftValue, HudText.Parts(ffa.Frame.Plate).RightValue));

        HudDemoFrames.TryGet("net", out HudDemo net);
        Assert.True(net.NetPanelOpen);
    }

    [Fact]
    public void The_scoreboard_screen_is_a_team_battle_with_mixed_teams_and_one_down_on_each_side()
    {
        HudDemoFrames.TryGet("scoreboard", out HudDemo demo);
        var rows = ScoreboardRows(demo.Frame);

        Assert.True(demo.ScoreboardOpen);
        Assert.Equal(BattleMode.TeamBattle, demo.Frame.Mode);
        Assert.Equal(8, rows.Count);
        Assert.Equal(4, rows.Count(r => r.Side != Side.Enemy));
        Assert.Equal(2, rows.Count(r => !r.Alive));
        Assert.Equal(0, rows[0].Team);   // the own team on top
        Assert.False(demo.Frame.Players.Where(p => p.Active).Select(p => p.Team).SequenceEqual(demo.Frame.Players.Where(p => p.Active).Select(p => p.Team).OrderBy(t => t)));   // slots are not grouped by team
    }

    [Fact]
    public void The_standard_screen_shows_both_bars_own_base_first()
    {
        HudDemoFrames.TryGet("standard", out HudDemo demo);
        MatchHudFrame f = demo.Frame;

        Assert.Equal(BattleMode.Standard, f.Mode);
        Assert.Equal(MatchPhase.Playing, f.Phase);
        Assert.Equal("6:40", HudText.Timer(f.TicksLeft));
        Assert.Equal(2, f.Capture.Count);
        Assert.Equal(new CaptureBar(OwnBase: true, 37), f.Capture[0]);
        Assert.Equal(new CaptureBar(OwnBase: false, 62), f.Capture[1]);
        Assert.Equal("我方基地 37", CaptureText.Label(f.Capture[0]));
        Assert.Equal("敌方基地 62", CaptureText.Label(f.Capture[1]));
        // The gallery's frozen clock: the alarm's first reading is only its baseline, so the own bar stays steady bright red.
        Assert.False(new CaptureAlarm().Update(f.Capture, now: 0));
        Assert.Equal(CaptureFill.Danger, MatchColors.FillOf(f.Capture[0], blinkDark: false));
        Assert.Equal(CaptureFill.Ally, MatchColors.FillOf(f.Capture[1], blinkDark: false));
        Assert.Equal(("3/4", "2/4"), (HudText.Parts(f.Plate).LeftValue, HudText.Parts(f.Plate).RightValue));
        Assert.Equal(2, demo.Kills.Length);
        Assert.False(demo.ScoreboardOpen);
    }

    [Fact]
    public void The_capture_screen_is_a_win_by_capture_with_the_capture_column()
    {
        HudDemoFrames.TryGet("capture", out HudDemo demo);
        MatchHudFrame f = demo.Frame;
        ResultsParts words = HudText.Results(f.Mode, f.OwnSlot, f.OwnTeam, f.Results, f.Plate, f.Players, f.Names);
        var rows = ScoreboardRows(f);

        Assert.Equal(BattleMode.Standard, f.Mode);
        Assert.Equal(MatchPhase.Ended, f.Phase);
        Assert.Equal(MatchEndReason.Captured, f.Results.Reason);
        Assert.Equal(0, f.Results.WinnerTeam);
        Assert.Equal("胜利", words.Title);
        Assert.Equal("占领了敌方基地", words.Subtitle);
        Assert.Equal(6, rows.Count);
        Assert.Equal(new[] { 41, 33, 26 }, rows.Where(r => r.Team == 0).Select(r => r.Capture));
        Assert.Equal(new[] { 0, 0, 12 }, rows.Where(r => r.Team == 1).Select(r => r.Capture).Order());
        Assert.All(rows, r => Assert.Equal(r.Capture.ToString(System.Globalization.CultureInfo.InvariantCulture), ScoreboardModel.CaptureCell(r, ended: true)));   // every cell a number once it is over
        Assert.InRange(f.Results.TicksLeft, 1, ModeRules.ResultsTicks);
    }

    [Theory]
    [InlineData("results-win", "胜利")]
    [InlineData("results-lose", "失败")]
    [InlineData("results-draw", "平局")]
    public void The_team_results_screens_show_their_verdict(string name, string title)
    {
        HudDemoFrames.TryGet(name, out HudDemo demo);
        MatchHudFrame f = demo.Frame;

        Assert.Equal(MatchPhase.Ended, f.Phase);
        Assert.False(demo.ScoreboardOpen);   // the results show the table by themselves
        Assert.Equal(title, HudText.Results(f.Mode, f.OwnSlot, f.OwnTeam, f.Results, f.Plate, f.Players, f.Names).Title);
        Assert.InRange(f.Results.TicksLeft, 1, ModeRules.ResultsTicks);
    }

    [Fact]
    public void The_free_for_all_results_rank_six_players_from_one_to_six_with_the_player_third()
    {
        HudDemoFrames.TryGet("results-ffa", out HudDemo demo);
        MatchHudFrame f = demo.Frame;
        var rows = ScoreboardRows(f);

        Assert.Equal(6, rows.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, rows.Select(r => r.Rank));
        Assert.Equal(f.Players.Where(p => p.Active).Select(p => (int)p.Rank).Order(), Enumerable.Range(1, 6));   // unique, from the server
        Assert.Equal(3, rows.Single(r => r.Side == Side.Own).Rank);
        Assert.Equal("第 3 名", HudText.Results(f.Mode, f.OwnSlot, f.OwnTeam, f.Results, f.Plate, f.Players, f.Names).Title);
        Assert.Equal("机器人 1 是最后的幸存者", HudText.Results(f.Mode, f.OwnSlot, f.OwnTeam, f.Results, f.Plate, f.Players, f.Names).Subtitle);
    }

    private static readonly string[] ScreensBeforeTheMap = { "timer", "feed", "respawn", "spectate", "spectate-team", "spectate-ffa", "annihilation", "ffa", "net", "scoreboard", "results-win", "results-lose", "results-draw", "results-ffa", "standard", "capture" };

    [Fact]
    public void The_map_screens_come_last_and_leave_the_others_as_they_were()
    {
        Assert.Equal(ScreensBeforeTheMap.Append("minimap").Append("map-full"), HudDemoFrames.Names);
        foreach (string name in ScreensBeforeTheMap)
        {
            HudDemoFrames.TryGet(name, out HudDemo demo);
            Assert.Null(demo.Minimap);
        }
    }

    [Theory]
    [InlineData("minimap", false)]
    [InlineData("map-full", true)]
    public void The_map_screens_carry_the_riverland_map_over_the_standard_screen(string name, bool fullOpen)
    {
        Assert.True(HudDemoFrames.TryGet(name, out HudDemo demo));
        HudDemoFrames.TryGet("standard", out HudDemo standard);

        Assert.NotNull(demo.Minimap);
        Assert.Equal(RiverlandMap.Id, demo.Minimap.MapId);
        Assert.Equal(fullOpen, demo.Minimap.FullOpen);
        Assert.Equal(BattleMode.Standard, demo.Frame.Mode);
        Assert.Equal(standard.Frame.Capture, demo.Frame.Capture);
        Assert.Equal(standard.Kills, demo.Kills);
    }

    [Fact]
    public void The_canned_map_frame_has_both_bases_seven_tanks_and_the_own_arrow()
    {
        HudDemoFrames.TryGet("minimap", out HudDemo demo);
        MinimapFrame frame = demo.Minimap!.Frame;
        MinimapTank[] tanks = frame.Tanks.ToArray();

        Assert.Equal(2, frame.Bases.Length);
        MinimapBase own = frame.Bases.ToArray().Single(b => b.Own), enemy = frame.Bases.ToArray().Single(b => !b.Own);
        Assert.Equal(RiverlandMap.Bases[0].Centre, own.Centre);
        Assert.Equal(RiverlandMap.Bases[1].Centre, enemy.Centre);
        Assert.Equal(37, own.Points);    // as the standard screen's bars: the enemy has taken 37 of ours
        Assert.Equal(62, enemy.Points);
        Assert.Equal(7, tanks.Length);
        Assert.Equal(4, tanks.Count(t => t.Side == Side.Ally));
        Assert.Equal(3, tanks.Count(t => t.Side == Side.Enemy));
        Assert.Equal(2, tanks.Count(t => t.Destroyed));
        Assert.Single(tanks, t => t.Side == Side.Ally && t.Destroyed);
        Assert.Single(tanks, t => t.Side == Side.Enemy && t.Destroyed);
        Assert.Equal(Enum.GetValues<MinimapShape>(), tanks.Where(t => t.Side == Side.Ally).Select(t => t.Shape).Order());   // one of each shape
        Assert.Equal(Enum.GetValues<MinimapShape>(), tanks.Where(t => !t.Destroyed).Select(t => t.Shape).Distinct().Order());   // and each drawn standing at least once
        Assert.Null(frame.OwnWreck);

        MinimapEye eye = frame.Eye;
        Assert.Equal(EyeKind.Arrow, eye.Kind);
        Vector2 towardsEnemy = Vector2.Normalize(enemy.Centre - own.Centre);
        Assert.Equal(120f, Vector2.Distance(own.Centre, eye.Position), 0.01f);
        Assert.True(Vector2.Dot(Vector2.Normalize(eye.Position - own.Centre), towardsEnemy) > 0.999f, "the arrow is in front of the own base");
        Assert.True(Vector2.Dot(Tank.Sim.Geometry.Angles.Forward(eye.Yaw), towardsEnemy) > 0.999f, "the arrow points at the enemy base");
        Assert.Equal(-0.4f, Tank.Sim.Geometry.Angles.Wrap(eye.CameraYaw - eye.Yaw), 0.001f);   // the camera looks 0.4 rad right of the hull
        Assert.Equal(0.9f, eye.HalfFov, 0.001f);
    }

    [Theory]
    [InlineData("minimap")]
    [InlineData("map-full")]
    public void Every_point_of_the_canned_map_frame_is_on_drivable_ground(string name)
    {
        HudDemoFrames.TryGet(name, out HudDemo demo);
        MinimapFrame frame = demo.Minimap!.Frame;
        float size = (RiverlandMap.Samples - 1) * RiverlandMap.CellSize;
        var points = frame.Tanks.ToArray().Select(t => t.Position).Concat(frame.Bases.ToArray().Select(b => b.Centre)).Append(frame.Eye.Position);

        foreach (Vector2 p in points)
        {
            Assert.InRange(p.X, RiverlandMap.Border, size - RiverlandMap.Border);
            Assert.InRange(p.Y, RiverlandMap.Border, size - RiverlandMap.Border);
        }
    }
}
