using Tank.Client.Bootstrap;

namespace Tank.Art.Tests;

public class LaunchOptionsMapTests
{
    [Fact]
    public void Map_takes_a_catalog_id()
    {
        Assert.Equal("riverland", LaunchOptions.Parse(new[] { "--map", "riverland" }).Map);
        Assert.Equal("riverland", LaunchOptions.Parse(new[] { "--map", "RiverLand" }).Map);   // stored in the catalog's spelling
        Assert.Equal("sandbox", LaunchOptions.Parse(new[] { "--map", "sandbox" }).Map);
        Assert.Null(LaunchOptions.Parse(Array.Empty<string>()).Map);
        Assert.Empty(LaunchOptions.Parse(new[] { "--map", "riverland" }).Warnings);
    }

    [Fact]
    public void An_unknown_map_warns_and_keeps_the_default()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--map", "moon" });
        Assert.Null(options.Map);
        Assert.Equal("Bad --map 'moon'; use sandbox|riverland. Using the default map.", Assert.Single(options.Warnings));
    }

    [Fact]
    public void A_map_without_a_value_is_ignored_with_the_usual_warning()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--map", "--sandbox" });
        Assert.Null(options.Map);
        Assert.True(options.Sandbox);
        Assert.Equal("--map needs a value but is followed by --sandbox; --map is ignored.", Assert.Single(options.Warnings));
    }
}
