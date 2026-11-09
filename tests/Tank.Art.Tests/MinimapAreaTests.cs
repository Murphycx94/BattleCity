using Tank.Client.Minimap;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using SN = System.Numerics;

namespace Tank.Art.Tests;

public class MinimapAreaTests
{
    private const float Tolerance = 1e-5f;

    /// <summary>A flat field of <paramref name="sizeX"/> × <paramref name="sizeZ"/> metres (2 m cells), no props.</summary>
    private static Battlefield FlatField(int sizeX, int sizeZ, float border)
    {
        const float cell = 2f;
        int samplesX = (int)(sizeX / cell) + 1, samplesZ = (int)(sizeZ / cell) + 1;
        return new Battlefield(new HeightField(samplesX, samplesZ, cell, new float[samplesX * samplesZ]), props: [], border);
    }

    private static void AssertNear(SN.Vector2 expected, SN.Vector2 actual)
    {
        Assert.True(MathF.Abs(expected.X - actual.X) <= Tolerance && MathF.Abs(expected.Y - actual.Y) <= Tolerance, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void The_riverland_s_area_is_its_playable_square()
    {
        Battlefield field = MapCatalog.Riverland.NewBattlefield();
        MinimapArea area = MinimapArea.Of(field);
        Assert.Equal(field.Terrain.SizeX - 2 * field.Border, area.Size);
        Assert.Equal(field.Border, area.MinX);
        Assert.Equal(field.Border, area.MinZ);
        float far = field.Terrain.SizeX - field.Border;
        AssertNear(new SN.Vector2(0, 0), area.ToUnit(new SN.Vector2(field.Border, field.Border)));
        AssertNear(new SN.Vector2(1, 0), area.ToUnit(new SN.Vector2(far, field.Border)));
        AssertNear(new SN.Vector2(0, 1), area.ToUnit(new SN.Vector2(field.Border, far)));
        AssertNear(new SN.Vector2(1, 1), area.ToUnit(new SN.Vector2(far, far)));
    }

    [Fact]
    public void Unit_and_ground_coordinates_round_trip()
    {
        MinimapArea area = MinimapArea.Of(MapCatalog.Riverland.NewBattlefield());
        foreach (SN.Vector2 unit in new SN.Vector2[] { new(0, 0), new(1, 1), new(0.25f, 0.75f), new(0.5f, 0.125f), new(-0.1f, 1.2f) })
            AssertNear(unit, area.ToUnit(area.ToGround(unit)));
    }

    [Fact]
    public void A_rectangular_field_grows_to_a_square_about_its_middle()
    {
        MinimapArea area = MinimapArea.Of(FlatField(300, 200, border: 10));
        // Playable: x 10 … 290 (280 m), z 10 … 190 (180 m). The square is 280 m; the short side grows 50 m at each end.
        Assert.Equal(280f, area.Size);
        Assert.Equal(10f, area.MinX);
        Assert.Equal(10f - 50f, area.MinZ);
        AssertNear(new SN.Vector2(0.5f, 0.5f), area.ToUnit(new SN.Vector2(150, 100)));
    }

    [Fact]
    public void Unit_u_runs_along_x_and_v_along_z()
    {
        MinimapArea area = MinimapArea.Of(FlatField(300, 200, border: 10));
        AssertNear(new SN.Vector2(1, 0), area.ToUnit(new SN.Vector2(area.MinX + area.Size, area.MinZ)));
        AssertNear(new SN.Vector2(0, 1), area.ToUnit(new SN.Vector2(area.MinX, area.MinZ + area.Size)));
        Assert.Equal(area.Size / 512, area.MetresPerPixel(512));
    }

    [Fact]
    public void Ground_outside_the_square_maps_outside_0_to_1()
    {
        MinimapArea area = MinimapArea.Of(FlatField(300, 200, border: 10));
        SN.Vector2 outside = area.ToUnit(new SN.Vector2(area.MinX - 1, area.MinZ + area.Size + 1));
        Assert.True(outside.X < 0 && outside.Y > 1, $"{outside}");
    }
}
