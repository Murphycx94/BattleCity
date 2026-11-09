using Tank.Client.World;

namespace Tank.Art.Tests;

public class RingBandTests
{
    [Fact]
    public void A_full_size_base_gets_the_whole_band_and_a_line_of_the_set_width()
    {
        RingBand band = RingBand.Around(25f);

        Assert.Equal(25f + RingBand.OuterOver, band.Outer, 5);
        Assert.Equal(25f + RingBand.OuterOver - RingBand.Width, band.Inner, 5);
        Assert.Equal(1f - RingBand.SolidLine / RingBand.Width, band.SolidFrom, 5);
        Assert.Equal(RingBand.SolidLine, (1f - band.SolidFrom) * (band.Outer - band.Inner), 5);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(0.5f)]
    [InlineData(0.1f)]
    public void A_small_base_s_band_stops_at_the_centre_and_its_line_keeps_the_set_width(float radius)
    {
        RingBand band = RingBand.Around(radius);

        Assert.Equal(0f, band.Inner);
        Assert.Equal(radius + RingBand.OuterOver, band.Outer, 5);
        // The line is measured on the band there is, not on the full width: it stays SolidLine metres wide, or covers
        // the whole band where that is narrower still.
        float width = band.Outer - band.Inner;
        Assert.Equal(MathF.Min(RingBand.SolidLine, width), (1f - band.SolidFrom) * width, 5);
        Assert.InRange(band.SolidFrom, 0f, 1f);
        if (width <= RingBand.SolidLine) Assert.Equal(0f, band.SolidFrom);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-3f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_radius_that_is_not_positive_and_finite_is_refused(float radius) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RingBand.Around(radius));
}
