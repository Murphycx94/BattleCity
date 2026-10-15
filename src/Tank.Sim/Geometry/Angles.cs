using System.Numerics;

namespace Tank.Sim.Geometry;

/// <summary>
/// Angle helpers. Yaw 0 faces -Z and positive yaw turns left (counter-clockwise seen from above), same as Godot's
/// Rotation.Y. Pitch is positive upward. Ground vectors are (x, z) packed into <see cref="Vector2"/>.
/// </summary>
public static class Angles
{
    public const float Pi = MathF.PI;
    public const float TwoPi = MathF.PI * 2;

    public static float Deg(float degrees) => degrees * (Pi / 180f);

    /// <summary>Wraps to (-π, π].</summary>
    public static float Wrap(float a)
    {
        a %= TwoPi;
        if (a <= -Pi) a += TwoPi;
        else if (a > Pi) a -= TwoPi;
        return a;
    }

    /// <summary>Turns <paramref name="current"/> toward <paramref name="target"/> along the shorter arc by at most <paramref name="maxStep"/>.</summary>
    public static float MoveTowards(float current, float target, float maxStep)
    {
        float diff = Wrap(target - current);
        if (MathF.Abs(diff) <= maxStep) return Wrap(target);
        return Wrap(current + MathF.CopySign(maxStep, diff));
    }

    /// <summary>Interpolates along the shorter arc.</summary>
    public static float Lerp(float a, float b, float t) => Wrap(a + Wrap(b - a) * t);

    public static Vector2 Forward(float yaw) => new(-MathF.Sin(yaw), -MathF.Cos(yaw));

    public static Vector2 Right(float yaw) => new(MathF.Cos(yaw), -MathF.Sin(yaw));

    /// <summary>Inverse of <see cref="Forward"/>: the yaw facing along the ground direction <paramref name="direction"/> (any length), in (-π, π].</summary>
    // `+ 0f` turns -0 into +0 so that straight back (+Z) yields +π instead of -π.
    public static float YawOf(Vector2 direction) => MathF.Atan2(-direction.X + 0f, -direction.Y);

    /// <summary>Unit 3D direction for a yaw and pitch.</summary>
    public static Vector3 Direction(float yaw, float pitch)
    {
        float c = MathF.Cos(pitch);
        return new Vector3(-MathF.Sin(yaw) * c, MathF.Sin(pitch), -MathF.Cos(yaw) * c);
    }

    /// <summary>
    /// Inverse of <see cref="Direction"/>; <paramref name="direction"/> need not be normalized. When the direction has no
    /// horizontal component (straight up/down, or zero) the yaw is undefined and <paramref name="fallbackYaw"/> is returned.
    /// </summary>
    public static (float Yaw, float Pitch) AimAngles(Vector3 direction, float fallbackYaw = 0f)
    {
        float length = direction.Length();
        if (length < 1e-6f) return (fallbackYaw, 0);
        float horizontal = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
        // `+ 0f` turns -0 into +0 so that straight back (+Z) yields +π instead of -π.
        float yaw = horizontal < 1e-6f * length ? fallbackYaw : Wrap(MathF.Atan2(-direction.X + 0f, -direction.Z));
        return (yaw, MathF.Asin(System.Math.Clamp(direction.Y / length, -1f, 1f)));
    }
}
