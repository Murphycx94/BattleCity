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
}
