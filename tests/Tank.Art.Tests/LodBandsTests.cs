using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class LodBandsTests
{
    private static readonly float[] Edges = { 28f, 52f, float.MaxValue };
    private const float H = 4f;

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(28f, 0)]
    [InlineData(28.1f, 1)]
    [InlineData(52f, 1)]
    [InlineData(500f, 2)]
    public void Without_a_current_band_the_plain_band_is_returned(float distance, int expected) =>
        Assert.Equal(expected, LodBands.Select(distance, -1, Edges, H));

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(0, 31.9f)]      // just past the edge, inside the hysteresis
    [InlineData(1, 24.1f)]      // just before the edge, inside the hysteresis
    [InlineData(1, 55.9f)]
    [InlineData(2, 48.1f)]
    public void Stays_in_the_current_band_within_the_hysteresis(int current, float distance) =>
        Assert.Equal(current, LodBands.Select(distance, current, Edges, H));

    [Theory]
    [InlineData(0, 32.1f, 1)]   // outward over the first edge
    [InlineData(1, 23.9f, 0)]   // inward over the first edge
    [InlineData(1, 56.1f, 2)]
    [InlineData(2, 47.9f, 1)]
    public void Switches_once_past_the_hysteresis(int current, float distance, int expected) =>
        Assert.Equal(expected, LodBands.Select(distance, current, Edges, H));

    [Theory]
    [InlineData(0, 90f, 2)]     // jumps two bands: no hysteresis applies
    [InlineData(2, 5f, 0)]
    [InlineData(0, 53f, 2)]
    [InlineData(2, 27f, 0)]
    public void A_jump_over_more_than_one_band_returns_the_new_band_immediately(int current, float distance, int expected) =>
        Assert.Equal(expected, LodBands.Select(distance, current, Edges, H));

    [Fact]
    public void Walking_outward_then_back_does_not_flicker()
    {
        int band = LodBands.Select(0f, -1, Edges, H);
        int switches = 0;
        for (float d = 20f; d <= 36f; d += 0.5f) switches += Step(ref band, d);
        for (float d = 36f; d >= 20f; d -= 0.5f) switches += Step(ref band, d);
        Assert.Equal(2, switches);

        static int Step(ref int band, float d)
        {
            int next = LodBands.Select(d, band, Edges, H);
            int changed = next != band ? 1 : 0;
            band = next;
            return changed;
        }
    }
}
