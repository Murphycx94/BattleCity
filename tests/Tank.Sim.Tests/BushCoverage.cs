using System.Numerics;

namespace Tank.Sim.Tests;

/// <summary>2D hiding probe: how much of a hull's visible edge is blocked, as seen from far away, by bush discs.</summary>
internal static class BushCoverage
{
    public const float HullWidth = 3.2f, HullLength = 6.5f, ViewDistance = 60f;
    public static readonly float[] Yaws = Enumerable.Range(0, 12).Select(i => i * MathF.PI / 12).ToArray();

    public static readonly float[] Directions = Enumerable.Range(0, 8).Select(i => i * MathF.PI / 4).ToArray();

    /// <summary>Fraction (0..1) of the hull's viewer-facing edge points that are blocked from <paramref name="viewer"/>.</summary>
    public static float Coverage((Vector2 C, float R)[] discs, Vector2 p, float yaw, Vector2 viewer)
    {
        var right = new Vector2(MathF.Cos(yaw), MathF.Sin(yaw));
        var fwd = new Vector2(-right.Y, right.X);
        float hw = HullWidth / 2, hl = HullLength / 2;
        var edges = new (Vector2 A, Vector2 B, Vector2 N)[]
        {
            (p - right * hw - fwd * hl, p - right * hw + fwd * hl, -right),
            (p + right * hw - fwd * hl, p + right * hw + fwd * hl, right),
            (p - right * hw - fwd * hl, p + right * hw - fwd * hl, -fwd),
            (p - right * hw + fwd * hl, p + right * hw + fwd * hl, fwd),
        };
        int total = 0, hidden = 0;
        foreach (var e in edges)
        {
            if (Vector2.Dot(e.N, viewer - (e.A + e.B) / 2) <= 0) continue;
            for (int i = 0; i <= 24; i++)
            {
                total++;
                if (Blocked(discs, viewer, Vector2.Lerp(e.A, e.B, i / 24f))) hidden++;
            }
        }
        return total == 0 ? 1 : (float)hidden / total;
    }

    /// <summary>Worst coverage over the eight compass directions; stops early once it drops below <paramref name="stopBelow"/>.</summary>
    public static float WorstOverDirections((Vector2 C, float R)[] discs, Vector2 p, float yaw, float stopBelow = 0f)
    {
        float worst = 1f;
        foreach (float phi in Directions)
        {
            var viewer = p + new Vector2(MathF.Cos(phi), MathF.Sin(phi)) * ViewDistance;
            worst = MathF.Min(worst, Coverage(discs, p, yaw, viewer));
            if (worst < stopBelow) break;
        }
        return worst;
    }

    /// <summary>Best hull pose near <paramref name="centre"/> (inside some disc's reach) over a 0.5 m grid and 15° yaws.</summary>
    public static (float Best, Vector2 Pos, float Yaw) BestHide((Vector2 C, float R)[] discs, Vector2 centre, float threshold)
    {
        float best = -1; Vector2 bp = default; float by = 0;
        for (float x = -12; x <= 12; x += 0.5f)
        for (float z = -12; z <= 12; z += 0.5f)
        {
            var p = centre + new Vector2(x, z);
            if (!discs.Any(d => Vector2.Distance(p, d.C) < d.R + 3.6f)) continue;
            foreach (float yaw in Yaws)
            {
                float worst = WorstOverDirections(discs, p, yaw, threshold);
                if (worst > best) { best = worst; bp = p; by = yaw; }
                if (best >= threshold) return (best, bp, by);
            }
        }
        return (best, bp, by);
    }

    private static bool Blocked((Vector2 C, float R)[] discs, Vector2 from, Vector2 to)
    {
        Vector2 d = to - from;
        float len = d.Length();
        d /= len;
        foreach (var disc in discs)
        {
            if (Vector2.DistanceSquared(to, disc.C) <= disc.R * disc.R) return true;
            Vector2 m = from - disc.C;
            float b = Vector2.Dot(m, d), c = m.LengthSquared() - disc.R * disc.R, h = b * b - c;
            if (h < 0) continue;
            float t = -b - MathF.Sqrt(h);
            if (t > 0 && t < len) return true;
        }
        return false;
    }
}
