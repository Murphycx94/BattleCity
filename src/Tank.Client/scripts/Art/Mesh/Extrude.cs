using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>Solid bodies swept from a convex outline. Built on <see cref="Prism"/>.</summary>
public static class Extrude
{
    /// <summary>X ↔ Z swap: turns the prism's Z axis into world X (a mirror, so the winding is flipped for us).</summary>
    private static readonly Matrix4x4 SwapXZ = new(0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1);

    /// <summary>Y ↔ Z swap: turns the prism's Z axis into world Y.</summary>
    private static readonly Matrix4x4 SwapYZ = new(1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1);

    /// <summary>
    /// Side profile swept along X. <paramref name="profileZY"/> holds (z, y) points of a convex outline as seen from the
    /// tank's left (−X) side, in either winding; the body spans x ∈ [−width/2, width/2].
    /// </summary>
    public static MeshData Build(IReadOnlyList<Vector2> profileZY, float width, float bevel = 0f) =>
        Prism.Build(profileZY, width, bevel).Transformed(SwapXZ);

    /// <summary>
    /// Top-view outline (x, z) raised from y = 0 to y = <paramref name="height"/>, shrunk by <paramref name="topScale"/>
    /// at the top. Used for turrets and cupola bases.
    /// </summary>
    public static MeshData Upward(IReadOnlyList<Vector2> outlineXZ, float height, float bevel = 0f, float topScale = 1f) =>
        Prism.Build(outlineXZ, height, bevel, topScale)
            .Transformed(SwapYZ * Matrix4x4.CreateTranslation(0, height / 2, 0));
}

/// <summary>Box centred on the origin with optionally chamfered edges.</summary>
public static class Box
{
    public static MeshData Build(Vector3 size, float bevel = 0f)
    {
        float hz = size.Z / 2, hy = size.Y / 2;
        var profile = new[] { new Vector2(-hz, -hy), new Vector2(hz, -hy), new Vector2(hz, hy), new Vector2(-hz, hy) };
        return Extrude.Build(profile, size.X, bevel);
    }
}
