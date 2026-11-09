using System.Numerics;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The "zone" camera preset (plan deviation 19; screenshots of the wall up close, a match only): the eye
/// <see cref="Inset"/> m inside the current circle on the side nearest the own tank, <see cref="EyeHeight"/> m over the
/// ground, looking out at the wall <see cref="TargetHeight"/> m up. Engine-neutral: y is up, (x, z) the ground.
/// </summary>
public static class ZoneCamera
{
    public const string Preset = "zone";

    /// <summary>m: TargetHeight was 20 at first; from 5 m up at 25 m that put the whole frame on the wall against the sky (additive light saturates to white there), 8 keeps the ground beyond the wall in view.</summary>
    public const float Inset = 25f, EyeHeight = 5f, TargetHeight = 8f;

    /// <summary>A circle smaller than this (the zone closed) is looked down on from <see cref="ClosedEyeHeight"/> over its centre.</summary>
    public const float MinRadius = 1f;
    public const float ClosedEyeHeight = 40f;

    /// <summary>
    /// The pose for <paramref name="current"/>: dir = normalize(near − centre) ((1, 0) when they coincide); eye = centre +
    /// dir × max(0, r − <see cref="Inset"/>) at groundAt + <see cref="EyeHeight"/>; target = centre + dir × r at groundAt +
    /// <see cref="TargetHeight"/>. Under <see cref="MinRadius"/>: the eye <see cref="ClosedEyeHeight"/> over the centre, the
    /// target the centre's ground. <paramref name="groundAt"/> is (x, z) → the ground (or water) height.
    /// </summary>
    public static (Vector3 Eye, Vector3 Target) Pose(in ZoneRing current, Vector2 near, Func<float, float, float> groundAt)
    {
        Vector2 centre = current.Centre;
        float r = current.Radius;
        if (r < MinRadius)
        {
            float ground = groundAt(centre.X, centre.Y);
            return (new Vector3(centre.X, ground + ClosedEyeHeight, centre.Y), new Vector3(centre.X, ground, centre.Y));
        }
        Vector2 offset = near - centre;
        float distance = offset.Length();
        Vector2 dir = distance > 1e-3f ? offset / distance : Vector2.UnitX;
        Vector2 eye = centre + dir * MathF.Max(0f, r - Inset);
        Vector2 target = centre + dir * r;
        return (new Vector3(eye.X, groundAt(eye.X, eye.Y) + EyeHeight, eye.Y), new Vector3(target.X, groundAt(target.X, target.Y) + TargetHeight, target.Y));
    }
}
