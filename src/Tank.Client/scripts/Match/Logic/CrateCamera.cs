using System.Numerics;
using Tank.Client.Art.Meshes;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The "crate" camera preset (plan deviation 38; screenshots of a crate up close, a royale only): no other preset frames
/// one, since crates stand at least 20 m from every spawn. The crate is the known, standing one nearest the tank (ties to
/// the lower id); the eye stands <see cref="EyeOut"/> m from it on the sign's front side, turned <see cref="Turn"/> off
/// the sign's facing (<see cref="CrateMesh.SignFacing"/>) for a three-quarter view, <see cref="EyeHeight"/> m over the
/// ground, looking at <see cref="FocusHeight"/> m over the crate's ground. Engine-neutral: y is up, (x, z) the ground.
/// </summary>
public static class CrateCamera
{
    public const string Preset = "crate";

    /// <summary>m: the eye's distance from the crate on the ground, and its height over the ground under it.</summary>
    public const float EyeOut = 3.6f, EyeHeight = 1.9f;

    /// <summary>m over the crate's ground: between the box's lid and the sign.</summary>
    public const float FocusHeight = 1.2f;

    /// <summary>rad: the eye's turn off the sign's facing (from +X towards +Z), so the box shows two sides.</summary>
    public const float Turn = MathF.PI / 6;

    /// <summary>
    /// The pose for the known, standing crate of <paramref name="crates"/> nearest <paramref name="near"/> (the tank):
    /// false when none is known, and the caller keeps its own camera. <paramref name="groundAt"/> is (x, z) → the ground
    /// (or water) height. Allocation-free.
    /// </summary>
    public static bool TryPose(CrateKnowledge crates, Vector2 near, Func<float, float, float> groundAt, out Vector3 eye, out Vector3 focus)
    {
        int best = -1;
        float bestSquared = float.MaxValue;
        ReadOnlySpan<CrateKnowledge.Entry> all = crates.All;
        for (int id = 0; id < all.Length; id++)
        {
            ref readonly CrateKnowledge.Entry e = ref all[id];
            if (!e.Known || !e.Standing) continue;
            float squared = Vector2.DistanceSquared(e.Position, near);
            if (squared >= bestSquared) continue;   // a tie keeps the lower id, met first
            (best, bestSquared) = (id, squared);
        }
        if (best < 0)
        {
            (eye, focus) = (default, default);
            return false;
        }
        Vector2 crate = all[best].Position;
        Vector2 facing = CrateMesh.SignFacing(best);
        (float sin, float cos) = MathF.SinCos(Turn);
        var dir = new Vector2(facing.X * cos - facing.Y * sin, facing.X * sin + facing.Y * cos);   // from +X towards +Z
        Vector2 at = crate + dir * EyeOut;
        eye = new Vector3(at.X, groundAt(at.X, at.Y) + EyeHeight, at.Y);
        focus = new Vector3(crate.X, groundAt(crate.X, crate.Y) + FocusHeight, crate.Y);
        return true;
    }
}
