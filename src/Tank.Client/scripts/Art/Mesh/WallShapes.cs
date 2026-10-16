using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Toy walls, fences and rubble (stage 2C design §4.1). Each mesh is in its prop's own frame: X along the wall, Y up from
/// the ground under its centre, Z across it; standing pieces reach <c>sink</c> below the ground like the props do.
/// Rubble carries a per-chunk shade in vertex colour R (0.8 … 1) so the chunks read apart.
/// </summary>
public static class WallShapes
{
    private const float PostInset = 0.18f;   // how far in from each end of the panel a fence post's centre stands
    private const float RailHeight = 0.14f, RailThickness = 0.07f;
    private static readonly float[] RailLevels = { 0.55f, 1.15f };   // rail centres over the ground, for a 1.5 m fence
    private const float FenceBevel = 0.025f;   // at most; thin parts get a fifth of their thinnest side
    private const int RubbleChunks = 16;
    private const float ChunkSizeMin = 0.28f, ChunkSizeSpread = 0.3f;   // a rubble chunk's length: 0.28 … 0.58 m

    /// <summary>A wall's top edges are rounded over this share of its thickness.</summary>
    public const float TopRoundShare = 0.28f;

    /// <summary>
    /// A fence post's square side, in metres; a fence thinner than this gets posts as thin as itself
    /// (<c>min(PostWidth, thickness)</c>), and a fallen panel lies on its posts' face (<see cref="FenceFall.Pose"/>).
    /// </summary>
    public const float PostWidth = 0.16f;

    /// <summary>
    /// A wall segment: a slab <paramref name="length"/> × <paramref name="thickness"/> from <paramref name="sink"/> below the
    /// ground up to <paramref name="height"/> above it. Its top edges are rounded over <see cref="TopRoundShare"/> of the
    /// thickness and its ends over half that (a choice: <see cref="RoundedSlab"/> would allow up to the top's own rounding),
    /// so segments laid end to end show a soft groove between them.
    /// </summary>
    public static MeshData Block(float length, float height, float thickness, float sink)
    {
        float hz = thickness / 2, top = TopRoundShare * thickness;
        var corners = new[] { new Vector2(-hz, -sink), new Vector2(hz, -sink), new Vector2(hz, height), new Vector2(-hz, height) };
        Vector2[] profile = Polygon2.RoundCorners(corners, new[] { 0f, 0f, top, top }, 4);
        return RoundedSlab.Build(profile, length, top / 2);
    }

    /// <summary>
    /// A fence panel: two square posts (<see cref="PostWidth"/>) from <paramref name="sink"/> below the ground to
    /// <paramref name="height"/>, a little in from each end, and two rails running the panel's whole
    /// <paramref name="length"/>, through the posts and past them to both ends; no thicker than <paramref name="thickness"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is too short for the two posts to stand apart.</exception>
    public static MeshData Fence(float length, float height, float thickness, float sink)
    {
        float post = MathF.Min(PostWidth, thickness);
        float shortest = 2 * PostInset + post;
        if (length < shortest)
            throw new ArgumentOutOfRangeException(nameof(length), length, $"A fence panel needs at least {shortest} m for its two posts to stand apart.");
        float postHeight = height + sink;
        var parts = new List<(MeshData, Matrix4x4)>();
        foreach (float side in new[] { -1f, 1f })
        {
            MeshData p = Box.Build(new Vector3(post, postHeight, post), Bevel(post));
            parts.Add((p, Matrix4x4.CreateTranslation(side * (length / 2 - PostInset), postHeight / 2 - sink, 0)));
        }
        foreach (float level in RailLevels)
        {
            float railThickness = MathF.Min(RailThickness, thickness);
            MeshData rail = Box.Build(new Vector3(length, RailHeight, railThickness), Bevel(railThickness));
            parts.Add((rail, Matrix4x4.CreateTranslation(0, level * height / 1.5f, 0)));
        }
        return Merge.Build(parts);
    }

    private static float Bevel(float thinnest) => MathF.Min(FenceBevel, thinnest * 0.2f);

    /// <summary>
    /// A low heap of chunks filling <paramref name="length"/> × <paramref name="depth"/> on the ground, at most
    /// <paramref name="height"/> tall, highest along the middle line and thinning out towards both edges: blocky bricks
    /// (<paramref name="blocky"/>) or rounded stones. The same <paramref name="seed"/> gives the same heap.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> or <paramref name="depth"/> is narrower than the biggest chunk.</exception>
    public static MeshData Rubble(float length, float depth, float height, bool blocky, uint seed)
    {
        float widest = 2 * ChunkHalf(ChunkSizeMin + ChunkSizeSpread, blocky).Length();   // the biggest chunk, turned any way
        if (length < widest)
            throw new ArgumentOutOfRangeException(nameof(length), length, $"A rubble heap needs at least {widest} m for its chunks.");
        if (depth < widest)
            throw new ArgumentOutOfRangeException(nameof(depth), depth, $"A rubble heap needs at least {widest} m for its chunks.");
        MeshData stone = Icosphere.Build(1);
        var parts = new List<(MeshData, Matrix4x4)>();
        for (int k = 0; k < RubbleChunks; k++)
        {
            float R(int salt) => SeededNoise.Hash01(k, salt, 7, seed);
            Vector3 half = ChunkHalf(ChunkSizeMin + ChunkSizeSpread * R(1), blocky);
            float reach = half.Length();   // the chunk stays inside the heap's box however it is turned
            float x = (R(2) * 2 - 1) * (length / 2 - reach);
            float across = (R(3) + R(4) - 1f) * (depth / 2 - reach);   // triangular spread: more in the middle
            float crest = height * (1f - MathF.Abs(across) / (depth / 2)) - reach;   // the heap's top above this spot, less the chunk
            float y = reach * 0.6f + MathF.Max(0f, crest - reach * 0.6f) * R(5);
            y = MathF.Min(y, height - reach);
            Matrix4x4 turn = Matrix4x4.CreateFromYawPitchRoll(R(6) * MathF.Tau, (R(7) - 0.5f) * 0.8f, (R(8) - 0.5f) * 0.8f);
            MeshData chunk = blocky ? Box.Build(half * 2, MathF.Min(half.Y, half.Z) * 0.35f) : stone.Transformed(Matrix4x4.CreateScale(half));
            float shade = 0.8f + 0.2f * R(9);
            parts.Add((chunk.WithColor(new Vector4(shade, 0, 0, 1)), turn * Matrix4x4.CreateTranslation(x, y, across)));
        }
        return Merge.Build(parts);
    }

    /// <summary>Half-extents of a rubble chunk <paramref name="size"/> long: a flat brick, or a plumper stone.</summary>
    private static Vector3 ChunkHalf(float size, bool blocky) =>
        (blocky ? new Vector3(size, size * 0.5f, size * 0.55f) : new Vector3(size, size * 0.7f, size * 0.85f)) * 0.5f;
}

/// <summary>
/// A fence panel toppling over (stage 2C design §4.1): it swings about its bottom edge on the side it falls to, speeding
/// up like a falling plank, and lies flat after <see cref="Seconds"/>.
/// </summary>
public static class FenceFall
{
    public const float Seconds = 0.35f;

    /// <summary>How far over it is after <paramref name="time"/> s: 0 standing, 1 flat on the ground.</summary>
    public static float Progress(float time)
    {
        float t = Math.Clamp(time / Seconds, 0f, 1f);
        return t * t;
    }

    /// <summary>
    /// The panel's pose in its own frame (row-vector convention, as <see cref="MeshData.Transformed"/>): turned about the
    /// bottom edge of its +Z face (<paramref name="side"/> +1) or −Z face (−1) by <paramref name="progress"/> × 90°.
    /// </summary>
    /// <param name="thickness">How thick the panel is at the ground: its posts' width (<see cref="WallShapes.PostWidth"/>,
    /// or the fence's thickness if thinner), so it turns about their face and comes to rest on it.</param>
    public static Matrix4x4 Pose(float progress, int side, float thickness)
    {
        float pivot = Math.Sign(side) * thickness / 2;
        float angle = Math.Sign(side) * progress * MathF.PI / 2;
        return Matrix4x4.CreateTranslation(0, 0, -pivot) * Matrix4x4.CreateRotationX(angle) * Matrix4x4.CreateTranslation(0, 0, pivot);
    }
}
