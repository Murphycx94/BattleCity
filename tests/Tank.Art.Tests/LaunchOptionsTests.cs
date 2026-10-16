using Tank.Client.Bootstrap;

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
        Assert.Contains("''", warning);
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
}
