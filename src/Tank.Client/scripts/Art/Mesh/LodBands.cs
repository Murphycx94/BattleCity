namespace Tank.Client.Art.Meshes;

/// <summary>Distance bands with hysteresis, so something sitting on a band edge doesn't flip back and forth.</summary>
public static class LodBands
{
    /// <summary>
    /// Band for <paramref name="distance"/>: the first whose far edge is not exceeded (the last edge should be infinite).
    /// Moving into a neighbouring band only counts once the distance is past the shared edge by
    /// <paramref name="hysteresis"/>; a jump over more than one band takes effect at once.
    /// </summary>
    /// <param name="current">The band in use now, or negative if there is none yet.</param>
    public static int Select(float distance, int current, ReadOnlySpan<float> farEdges, float hysteresis)
    {
        int band = 0;
        while (band < farEdges.Length - 1 && distance > farEdges[band]) band++;
        if (current < 0 || current >= farEdges.Length || Math.Abs(band - current) != 1) return band;
        // Adjacent bands share the edge that is the far edge of the nearer one.
        float edge = farEdges[Math.Min(band, current)];
        bool pastEdge = band > current ? distance > edge + hysteresis : distance < edge - hysteresis;
        return pastEdge ? band : current;
    }
}
