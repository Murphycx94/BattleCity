using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Square sectors of <see cref="SectorSize"/> m on the ground, keyed by (x, z) index: sector (i, j) covers
/// x ∈ [i·SectorSize, (i+1)·SectorSize), z likewise, negatives too. The forests group their trees by it, so frustum culling
/// and distance fading drop whole sectors, and a per-frame check only looks at the sectors it needs (riverland design §6.4);
/// the houses likewise (§6.5).
/// </summary>
public readonly record struct SectorGrid(float SectorSize)
{
    /// <summary>The sector holding (x, z).</summary>
    public (int X, int Z) KeyOf(float x, float z) => ((int)MathF.Floor(x / SectorSize), (int)MathF.Floor(z / SectorSize));

    /// <summary>
    /// Writes the keys of the sectors that the segment <paramref name="from"/> → <paramref name="to"/> meets when widened
    /// by <paramref name="margin"/> on every side (each sector grown by the margin and clipped against the segment: a
    /// little more than the rounded capsule at the corners), row by row; returns how many. At most
    /// <paramref name="keys"/>.Length are written, the rest left out. Allocates nothing.
    /// </summary>
    public int Touching(Vector2 from, Vector2 to, float margin, Span<(int X, int Z)> keys)
    {
        int count = 0;
        Collect(from, to, margin, keys, ref count);
        return count;
    }

    /// <summary>
    /// The sectors a view looks in for things in its way (the bushes, and the trees and houses that fade,
    /// <c>SeeThroughFades</c>), each once: those within <paramref name="near"/> + <paramref name="reach"/> of the eye, and
    /// with a tank those within <paramref name="reach"/> of the ground line from the eye to it. <paramref name="reach"/> is
    /// how far a thing reaches out of the sector it is filed under. Returns how many were written, or −1 when they don't
    /// all fit in <paramref name="keys"/>: the caller then looks at everything rather than miss a sector. Allocates nothing.
    /// </summary>
    public int InView(Vector2 eye, Vector2? tank, float near, float reach, Span<(int X, int Z)> keys)
    {
        int count = 0;
        if (!Collect(eye, eye, near + reach, keys, ref count)) return -1;
        if (tank is { } at && !Collect(eye, at, reach, keys, ref count)) return -1;
        return count;
    }

    /// <summary>
    /// Adds to <paramref name="keys"/>[..<paramref name="count"/>] the keys <see cref="Touching"/> finds that aren't there
    /// yet, row by row; false (with those that fitted written) when one doesn't fit.
    /// </summary>
    private bool Collect(Vector2 from, Vector2 to, float margin, Span<(int X, int Z)> keys, ref int count)
    {
        (int x0, int z0) = KeyOf(MathF.Min(from.X, to.X) - margin, MathF.Min(from.Y, to.Y) - margin);
        (int x1, int z1) = KeyOf(MathF.Max(from.X, to.X) + margin, MathF.Max(from.Y, to.Y) + margin);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            var min = new Vector2(x * SectorSize - margin, z * SectorSize - margin);
            var max = new Vector2((x + 1) * SectorSize + margin, (z + 1) * SectorSize + margin);
            if (!Crosses(from, to, min, max) || keys[..count].Contains((x, z))) continue;
            if (count == keys.Length) return false;
            keys[count++] = (x, z);
        }
        return true;
    }

    /// <summary>
    /// The indices into <paramref name="props"/> of those <paramref name="which"/> picks, grouped by the sector of their
    /// footprint's centre, ascending within each sector.
    /// </summary>
    public static Dictionary<(int X, int Z), List<int>> GroupByCentre(IReadOnlyList<Prop> props, Func<Prop, bool> which, float sectorSize)
    {
        var grid = new SectorGrid(sectorSize);
        var sectors = new Dictionary<(int X, int Z), List<int>>();
        for (int i = 0; i < props.Count; i++)
        {
            if (!which(props[i])) continue;
            Vector2 centre = props[i].Footprint.Center;
            var key = grid.KeyOf(centre.X, centre.Y);
            if (!sectors.TryGetValue(key, out List<int>? list)) sectors[key] = list = new List<int>();
            list.Add(i);
        }
        return sectors;
    }

    /// <summary>
    /// Whether the segment a → b meets the axis-aligned box [min, max] (Liang–Barsky clipping); a point segment is just
    /// tested inside. The houses use it, in a house's own frame (<see cref="Crosses(in Obb2, Vector2, Vector2)"/>), for the line
    /// from the camera to the tank.
    /// </summary>
    public static bool Crosses(Vector2 a, Vector2 b, Vector2 min, Vector2 max)
    {
        Vector2 d = b - a;
        float enter = 0f, leave = 1f;
        return Clip(-d.X, a.X - min.X, ref enter, ref leave) && Clip(d.X, max.X - a.X, ref enter, ref leave)
            && Clip(-d.Y, a.Y - min.Y, ref enter, ref leave) && Clip(d.Y, max.Y - a.Y, ref enter, ref leave);
    }

    /// <summary>Whether the segment a → b meets <paramref name="box"/>: <see cref="Crosses(Vector2, Vector2, Vector2, Vector2)"/> in the box's own frame.</summary>
    public static bool Crosses(in Obb2 box, Vector2 a, Vector2 b)
    {
        Vector2 right = box.AxisRight, forward = box.AxisForward, fromA = a - box.Center, fromB = b - box.Center;
        return Crosses(new Vector2(Vector2.Dot(fromA, right), Vector2.Dot(fromA, forward)),
            new Vector2(Vector2.Dot(fromB, right), Vector2.Dot(fromB, forward)), -box.HalfExtents, box.HalfExtents);
    }

    /// <summary>One slab of the clip: keeps the part of [enter, leave] where p·t ≤ q.</summary>
    private static bool Clip(float p, float q, ref float enter, ref float leave)
    {
        if (p == 0f) return q >= 0f;
        float t = q / p;
        if (p < 0f)
        {
            if (t > leave) return false;
            if (t > enter) enter = t;
        }
        else
        {
            if (t < enter) return false;
            if (t < leave) leave = t;
        }
        return true;
    }
}
