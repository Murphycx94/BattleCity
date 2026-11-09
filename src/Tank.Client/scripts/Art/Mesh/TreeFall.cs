using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// A felled tree going over (riverland design §6.4): it turns about its root, the way it was pushed, slowly at first and
/// faster as it goes (like <see cref="FenceFall"/>), and after <see cref="Seconds"/> lies at <see cref="RestAngle"/>: the
/// crown on the ground, the root end a little up.
/// </summary>
public static class TreeFall
{
    public const float Seconds = 1.2f;
    /// <summary>About 85°, radians.</summary>
    public const float RestAngle = 1.48f;
    private const uint DirectionSeed = 3571u;

    /// <summary>How far over the tree is <paramref name="seconds"/> after it was felled, radians: 0 standing, <see cref="RestAngle"/> at rest.</summary>
    public static float Angle(float seconds)
    {
        float t = Math.Clamp(seconds / Seconds, 0f, 1f);
        return RestAngle * t * t;
    }

    /// <summary>
    /// The level unit axis the tree turns about: (push.Y, 0, −push.X) normalised, so turning its up axis about it by a
    /// positive angle (right-handed) leans the tree along <paramref name="push"/>. Without a push the direction comes from
    /// <paramref name="propId"/>, so the tree falls the same way every time and on every machine.
    /// </summary>
    public static Vector3 Axis(Vector2 push, int propId)
    {
        Vector2 along;
        if (push.LengthSquared() > 1e-12f) along = Vector2.Normalize(push);
        else
        {
            float bearing = SeededNoise.Hash01(propId, 0, 0, DirectionSeed) * MathF.Tau;
            along = new Vector2(MathF.Cos(bearing), MathF.Sin(bearing));
        }
        return new Vector3(along.Y, 0f, -along.X);
    }

    /// <summary>The level unit direction a tree turning about <paramref name="axis"/> (<see cref="Axis"/>) falls: the way its crown goes.</summary>
    public static Vector2 Along(Vector3 axis) => new(-axis.Z, axis.X);

    /// <summary>
    /// A felled tree's whole transform <paramref name="seconds"/> after it went (row-vector convention, as
    /// <see cref="MeshData.Transformed"/>): its <paramref name="upright"/> one (its yaw and scale, then its root's place)
    /// turned about its root by <see cref="Angle"/> about <paramref name="axis"/>.
    /// </summary>
    public static Matrix4x4 Pose(Matrix4x4 upright, Vector3 axis, float seconds)
    {
        Vector3 root = upright.Translation;
        Matrix4x4 shape = upright with { Translation = Vector3.Zero };
        return shape * Matrix4x4.CreateFromAxisAngle(axis, Angle(seconds)) * Matrix4x4.CreateTranslation(root);
    }
}
