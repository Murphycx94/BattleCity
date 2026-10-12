using Tank.Core.World;

namespace Tank.Core.Tests;

public sealed class MapTests
{
    [Fact]
    public void Parse_ExpandsHalvesToCells_AndFindsMarkers()
    {
        var def = MapParser.Parse("m", "#@..\n..P.\nE.S.\n....");

        Assert.Equal(8, def.WidthCells);
        Assert.Equal(8, def.HeightCells);
        var map = def.CreateMap();
        Assert.Equal(CellType.Brick, map.Get(0, 0));
        Assert.Equal(CellType.Brick, map.Get(1, 1));
        Assert.Equal(CellType.Steel, map.Get(2, 0));
        Assert.Equal(CellType.Empty, map.Get(4, 0));
        Assert.Equal(new SpawnPoint(2 * Units.Half, Units.Half, 0), def.PlayerSpawns.Single());
        Assert.Equal(Teams.Enemy, def.EnemySpawns.Single().Team);
        Assert.Equal(new EagleSpawn(0, 2 * Units.Half, 0), def.Eagles.Single());
    }

    [Theory]
    [InlineData("..\n.", "rows must all")]
    [InlineData("P.X.\n....", "unknown character")]
    [InlineData("....\n....", "no player spawn")]
    [InlineData("P..\n...", "even number")]
    public void Parse_RejectsMalformedMaps(string text, string expectedMessage)
    {
        var ex = Assert.Throws<FormatException>(() => MapParser.Parse("bad", text));
        Assert.Contains(expectedMessage, ex.Message);
    }

    [Fact]
    public void Parse_IgnoresCommentsAndBlankLines()
    {
        var def = MapParser.Parse("m", "; comment\n\nP.\n..\n");
        Assert.Equal(4, def.WidthCells);
    }

    [Fact]
    public void Library_ContainsAllModeDefaults_AndTheyAreValid()
    {
        foreach (var mode in Enum.GetValues<GameMode>())
        {
            var def = MapLibrary.Load(MapLibrary.DefaultFor(mode));
            Assert.NotEmpty(def.PlayerSpawns);
            if (mode == GameMode.Coop)
            {
                Assert.NotEmpty(def.EnemySpawns);
                Assert.Contains(def.Eagles, e => e.Team == 0);
            }
            if (mode == GameMode.BaseBattle)
            {
                Assert.Contains(def.Eagles, e => e.Team == 1);
                Assert.Contains(def.PlayerSpawns, s => s.Team == 1);
            }
        }
    }

    [Fact]
    public void Library_UnknownMap_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => MapLibrary.Load("nope"));
    }

    [Fact]
    public void Map_RecordsChanges_AndChecksumTracksContent()
    {
        var map = new GameMap(4, 4);
        uint before = map.ComputeChecksum();

        map.Set(1, 2, CellType.Brick);
        map.Set(1, 2, CellType.Brick); // no-op, must not record twice

        Assert.Single(map.Changes);
        Assert.Equal(new CellChange(9, CellType.Brick), map.Changes[0]);
        Assert.NotEqual(before, map.ComputeChecksum());
        Assert.Equal(CellType.Steel, map.Get(-1, 0)); // outside counts as solid
    }

    [Fact]
    public void Map_Clone_IsIndependent()
    {
        var map = new GameMap(2, 2);
        var copy = map.Clone();
        copy.Set(0, 0, CellType.Water);
        Assert.Equal(CellType.Empty, map.Get(0, 0));
    }
}
