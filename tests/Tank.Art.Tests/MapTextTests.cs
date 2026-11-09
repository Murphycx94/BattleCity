using Tank.Client.UI;

namespace Tank.Art.Tests;

public class MapTextTests
{
    [Fact]
    public void Map_names_come_from_the_catalog()
    {
        Assert.Equal("试驾场地图", MapText.Name("sandbox"));
        Assert.Equal("河谷", MapText.Name("riverland"));
        Assert.Equal("河谷", MapText.Name("RIVERLAND"));
        Assert.Equal("moon", MapText.Name("moon"));
        Assert.Equal("—", MapText.Name(null));
        Assert.Equal("—", MapText.Name(""));
    }

    [Fact]
    public void Test_drive_titles()
    {
        Assert.Equal("坦克 3D · 试驾场", MapText.SandboxTitle("sandbox"));
        Assert.Equal("坦克 3D · 试驾场 · 河谷", MapText.SandboxTitle("riverland"));
    }

    [Fact]
    public void Match_titles()
    {
        Assert.Equal("坦克 3D · 河谷", MapText.MatchTitle("riverland"));
        Assert.Equal("坦克 3D · 试驾场地图", MapText.MatchTitle("sandbox"));
        Assert.Equal("坦克 3D · 对局", MapText.MatchTitle(null));
        Assert.Equal("坦克 3D · 对局", MapText.MatchTitle("moon"));
    }
}
