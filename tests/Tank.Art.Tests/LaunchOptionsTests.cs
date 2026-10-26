using Tank.Client.Bootstrap;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void Tank_names_a_catalog_tank()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--tank", "medium" });
        Assert.Equal("medium", options.Tank);
        Assert.Empty(options.Warnings);
    }

    [Fact]
    public void Spot_debug_is_off_unless_asked_for()
    {
        Assert.False(LaunchOptions.Parse(Array.Empty<string>()).SpotDebug);
        LaunchOptions options = LaunchOptions.Parse(new[] { "--spot-debug", "--camera-preset", "patrol" });
        Assert.True(options.SpotDebug);
        Assert.Equal("patrol", options.CameraPreset);
    }

    [Fact]
    public void No_tank_by_default()
    {
        Assert.Null(LaunchOptions.Parse(Array.Empty<string>()).Tank);
    }

    [Fact]
    public void Unknown_tank_is_ignored_with_a_warning_naming_the_known_ones()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--tank", "tiger" });
        Assert.Null(options.Tank);
        string warning = Assert.Single(options.Warnings);
        Assert.Contains("'tiger'", warning);
        Assert.Contains("medium", warning);
        Assert.Contains("Using medium", warning);
    }

    [Fact]
    public void Tank_as_the_last_argument_without_a_value_is_ignored_with_a_warning()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--tank" });
        Assert.Null(options.Tank);
        string warning = Assert.Single(options.Warnings);
        Assert.Contains("--tank needs a value", warning);
    }

    [Theory]
    [InlineData("prop:1.5", "prop", 1.5)]
    [InlineData("down:0", "down", 0)]
    [InlineData("kill:2", "kill", 2)]
    public void Shot_after_waits_for_a_wall_hurt_or_a_prop_down_too(string value, string moment, double delay)
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--shot-after", value });
        Assert.Equal((moment, delay), (options.ShotAfterEvent, options.ShotAfterDelay));
        Assert.Empty(options.Warnings);
    }

    [Fact]
    public void Shot_after_an_unknown_moment_is_ignored_with_a_warning_naming_the_known_ones()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--shot-after", "wall:1" });
        Assert.Null(options.ShotAfterEvent);
        Assert.Contains("fire|hit|kill|prop|down", Assert.Single(options.Warnings));
    }

    [Theory]
    [InlineData("teambattle", BattleMode.TeamBattle)]
    [InlineData("annihilation", BattleMode.Annihilation)]
    [InlineData("ffa", BattleMode.FreeForAll)]
    [InlineData("TeamBattle", BattleMode.TeamBattle)]
    [InlineData("FFA", BattleMode.FreeForAll)]
    [InlineData("standard", BattleMode.Standard)]
    [InlineData("Standard", BattleMode.Standard)]
    public void Mode_takes_the_battle_mode_ids_in_any_case(string value, BattleMode expected)
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--mode", value });
        Assert.Equal(expected, options.Mode);
        Assert.Empty(options.Warnings);
    }

    [Fact]
    public void Mode_is_left_to_the_server_unless_given()
    {
        Assert.Null(LaunchOptions.Parse(Array.Empty<string>()).Mode);
    }

    [Theory]
    [InlineData("coop")]   // a mode the first version of the game had
    [InlineData("")]
    public void A_bad_mode_warns_with_the_valid_ones_and_leaves_the_default(string value)
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--mode", value });
        Assert.Null(options.Mode);
        string warning = Assert.Single(options.Warnings);
        Assert.Contains("teambattle|annihilation|ffa", warning);
    }

    [Fact]
    public void A_bad_mode_warning_lists_all_four_modes()
    {
        string warning = Assert.Single(LaunchOptions.Parse(new[] { "--mode", "nope" }).Warnings);

        Assert.Contains("teambattle|annihilation|ffa|standard", warning);
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("0", 0)]
    [InlineData("9", 9)]
    public void Bots_in_range_pass_without_a_warning(string value, int expected)
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--bots", value });
        Assert.Equal(expected, options.Bots);
        Assert.Empty(options.Warnings);
    }

    [Theory]
    [InlineData("10", 9)]
    [InlineData("99", 9)]
    [InlineData("-2", 0)]
    public void Bots_out_of_range_are_clamped_with_a_warning(string value, int expected)
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--bots", value });
        Assert.Equal(expected, options.Bots);
        Assert.Contains("out of range", Assert.Single(options.Warnings));
    }

    [Fact]
    public void Unreadable_bots_become_zero_with_a_warning()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--bots", "many" });
        Assert.Equal(0, options.Bots);
        Assert.Contains("'many'", Assert.Single(options.Warnings));
    }

    [Fact]
    public void Players_default_to_one_and_must_be_at_least_one()
    {
        Assert.Equal(1, LaunchOptions.Parse(Array.Empty<string>()).Players);
        Assert.Equal(3, LaunchOptions.Parse(new[] { "--players", "3" }).Players);
        foreach (string bad in new[] { "0", "-1", "two" })
        {
            LaunchOptions options = LaunchOptions.Parse(new[] { "--players", bad });
            Assert.Equal(1, options.Players);
            Assert.Contains("--players", Assert.Single(options.Warnings));
        }
    }

    [Fact]
    public void Seed_is_an_unsigned_number_and_a_bad_one_warns()
    {
        Assert.Null(LaunchOptions.Parse(Array.Empty<string>()).Seed);
        Assert.Equal(42u, LaunchOptions.Parse(new[] { "--seed", "42" }).Seed);
        Assert.Equal(uint.MaxValue, LaunchOptions.Parse(new[] { "--seed", "4294967295" }).Seed);
        foreach (string bad in new[] { "-1", "4294967296", "x" })
        {
            LaunchOptions options = LaunchOptions.Parse(new[] { "--seed", bad });
            Assert.Null(options.Seed);
            Assert.Contains("--seed", Assert.Single(options.Warnings));
        }
    }

    [Fact]
    public void Loopback_and_sandbox_are_switches_that_are_off_by_default()
    {
        LaunchOptions none = LaunchOptions.Parse(Array.Empty<string>());
        Assert.False(none.Loopback);
        Assert.False(none.Sandbox);
        LaunchOptions both = LaunchOptions.Parse(new[] { "--loopback", "--sandbox" });
        Assert.True(both.Loopback);
        Assert.True(both.Sandbox);
    }

    [Fact]
    public void Match_seconds_must_be_above_zero()
    {
        Assert.Null(LaunchOptions.Parse(Array.Empty<string>()).MatchSeconds);
        Assert.Equal(12.5, LaunchOptions.Parse(new[] { "--match-seconds", "12.5" }).MatchSeconds);
        foreach (string bad in new[] { "0", "-3", "soon", "NaN", "100000" })
        {
            LaunchOptions options = LaunchOptions.Parse(new[] { "--match-seconds", bad });
            Assert.Null(options.MatchSeconds);
            Assert.Contains("--match-seconds", Assert.Single(options.Warnings));
        }
    }

    [Fact]
    public void Hud_demo_takes_a_name()
    {
        Assert.Null(LaunchOptions.Parse(Array.Empty<string>()).HudDemo);
        Assert.Equal("scoreboard", LaunchOptions.Parse(new[] { "--hud-demo", "scoreboard" }).HudDemo);
    }

    [Fact]
    public void Netgraph_means_the_panel_starts_open_and_the_camera_switch_is_gone()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--netgraph", "--camera", "chase" });
        Assert.True(options.NetGraph);
        Assert.Contains("Unknown option --camera", Assert.Single(options.Warnings));   // its value goes unread, without a second warning
    }

    [Theory]
    [InlineData("--autostrat")]
    [InlineData("--players2")]
    public void An_unknown_switch_warns_and_the_rest_still_parse(string typo)
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--host", typo, "--bots", "2" });
        Assert.True(options.Host);
        Assert.Equal(2, options.Bots);
        Assert.Contains(typo, Assert.Single(options.Warnings));
    }

    [Theory]
    [InlineData("--screenshot")]
    [InlineData("--name")]
    [InlineData("--bots")]
    [InlineData("--mode")]
    [InlineData("--shot-after")]
    public void A_switch_followed_by_another_switch_instead_of_its_value_warns_once_and_keeps_its_default(string option)
    {
        LaunchOptions defaults = LaunchOptions.Parse(Array.Empty<string>());
        LaunchOptions options = LaunchOptions.Parse(new[] { option, "--screenshot-at", "20" });

        Assert.Equal(20, options.ScreenshotAt);   // the next switch is read as a switch, with its own value
        Assert.Equal((defaults.ScreenshotPath, defaults.Name, defaults.Bots, defaults.Mode, defaults.ShotAfterEvent),
            (options.ScreenshotPath, options.Name, options.Bots, options.Mode, options.ShotAfterEvent));
        string warning = Assert.Single(options.Warnings);
        Assert.Contains($"{option} needs a value", warning);
        Assert.Contains("--screenshot-at", warning);
    }

    [Fact]
    public void A_bare_double_dash_is_passed_over_quietly()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--", "--host", "--bots", "3" });   // what "godot … -- -- --host" hands over
        Assert.True(options.Host);
        Assert.Equal(3, options.Bots);
        Assert.Empty(options.Warnings);
    }

    [Fact]
    public void A_negative_number_is_a_value_not_a_switch()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--bots", "-2" });
        Assert.Equal(0, options.Bots);
        Assert.Contains("out of range", Assert.Single(options.Warnings));
    }

    [Fact]
    public void Players_above_a_full_room_are_clamped_with_a_warning()
    {
        Assert.Equal(BattleProtocol.MaxPlayers, LaunchOptions.Parse(new[] { "--players", BattleProtocol.MaxPlayers.ToString() }).Players);
        LaunchOptions options = LaunchOptions.Parse(new[] { "--players", "11" });
        Assert.Equal(BattleProtocol.MaxPlayers, options.Players);
        Assert.Contains("--players 11", Assert.Single(options.Warnings));
    }

    [Theory]
    [InlineData("death:2", "death", 2)]
    [InlineData("ended:0.5", "ended", 0.5)]
    public void Shot_after_waits_for_the_own_death_or_the_end_of_the_match(string value, string moment, double delay)
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--shot-after", value });
        Assert.Equal((moment, delay), (options.ShotAfterEvent, options.ShotAfterDelay));
        Assert.Empty(options.Warnings);
    }

    [Fact]
    public void Port_takes_a_number_in_range_and_a_bad_one_warns_and_keeps_the_default()
    {
        Assert.Equal(47555, LaunchOptions.Parse(new[] { "--port", "47555" }).Port);
        int fallback = LaunchOptions.Parse(Array.Empty<string>()).Port;
        foreach (string bad in new[] { "0", "-1", "65536", "lan" })
        {
            LaunchOptions options = LaunchOptions.Parse(new[] { "--port", bad });
            Assert.Equal(fallback, options.Port);
            Assert.Contains("--port", Assert.Single(options.Warnings));
        }
    }

    [Fact]
    public void Bad_timer_values_warn_and_keep_their_defaults()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--screenshot-at", "soon", "--quit-after", "later", "--fx-shot", "now" });
        Assert.Equal(6, options.ScreenshotAt);
        Assert.Equal(0, options.QuitAfter);
        Assert.Equal(0.05, options.FxShotDelay);
        Assert.Equal(3, options.Warnings.Count);
        Assert.Contains(options.Warnings, w => w.Contains("--screenshot-at"));
        Assert.Contains(options.Warnings, w => w.Contains("--quit-after"));
        Assert.Contains(options.Warnings, w => w.Contains("--fx-shot"));
    }

    [Fact]
    public void The_host_options_of_a_smoke_run_parse_together()
    {
        LaunchOptions options = LaunchOptions.Parse(new[]
        {
            "--host", "--loopback", "--mode", "ffa", "--bots", "4", "--players", "1", "--seed", "9", "--match-seconds", "20",
            "--autostart", "--tank", "heavy", "--port", "47555",
        });
        Assert.True(options.Host && options.Loopback && options.AutoStart);
        Assert.Equal((BattleMode.FreeForAll, 4, 1, 9u, 20.0, "heavy", 47555),
            (options.Mode!.Value, options.Bots, options.Players, options.Seed!.Value, options.MatchSeconds!.Value, options.Tank, options.Port));
        Assert.Empty(options.Warnings);
    }
}
