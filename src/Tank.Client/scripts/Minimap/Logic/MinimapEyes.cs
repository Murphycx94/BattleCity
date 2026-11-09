using System.Numerics;
using Tank.Client.Match;
using Tank.Net.Battle;

namespace Tank.Client.Minimap;

/// <summary>The frame's eye, the own wreck's cross (null when the eye is on the own tank or there is none) and the tank the eye rides (0: none of the others).</summary>
public readonly record struct MinimapEyeChoice(MinimapEye Eye, Vector2? OwnWreck, int EyeTankId);

/// <summary>
/// Where the map's eye goes in a match (stage 5 design §5), and the camera's heading and horizontal half-angle the fan
/// opens by. Pure: the scene reads its state and the camera, this picks.
/// </summary>
public static class MinimapEyes
{
    /// <summary>Below this much horizontal reach the camera looks straight down, and its screen's up is its heading.</summary>
    private const float StraightDown = 1e-3f;

    /// <summary>
    /// The eye: none without an own tank; an arrow on the own tank while it fights (or a fixed preset frames it, dead or
    /// not); dead and watching, an arrow on <paramref name="followed"/> when the spectate chain follows it, else a ring on
    /// the free camera's <paramref name="focus"/> or on the own wreck (the close-up) — with the own wreck's cross.
    /// </summary>
    /// <param name="watching">The own tank is a wreck and the spectate chain has the camera (no fixed preset).</param>
    /// <param name="followed">The tank the chain follows, as interpolated; null when it is not in the snapshot.</param>
    /// <param name="ownPosition">The own tank as drawn (x, z), and <paramref name="ownYaw"/> its hull's heading.</param>
    public static MinimapEyeChoice Pick(bool hasTank, bool watching, SpectateState.Mode spectate, RemoteTank? followed,
        Vector2 ownPosition, float ownYaw, Vector2 focus, float cameraYaw, float halfFov)
    {
        if (!hasTank) return default;
        if (!watching) return new MinimapEyeChoice(new MinimapEye(EyeKind.Arrow, ownPosition, ownYaw, cameraYaw, halfFov), null, 0);
        if (spectate == SpectateState.Mode.Follow && followed is { } f)
            return new MinimapEyeChoice(new MinimapEye(EyeKind.Arrow, f.Position, f.Yaw, cameraYaw, halfFov), ownPosition, f.Entity.Id);
        Vector2 ring = spectate == SpectateState.Mode.Free ? focus : ownPosition;   // a vanished followed tank: the close-up's
        return new MinimapEyeChoice(new MinimapEye(EyeKind.Ring, ring, 0f, cameraYaw, halfFov), ownPosition, 0);
    }

    /// <summary>
    /// The horizontal half-angle, radians, of a camera whose vertical field of view is <paramref name="verticalFovDegrees"/>
    /// (Godot's <c>Camera3D.Fov</c>, which keeps the height) on a screen <paramref name="aspect"/> = width / height wide.
    /// </summary>
    public static float HalfFov(float verticalFovDegrees, float aspect) =>
        MathF.Atan(MathF.Tan(verticalFovDegrees * (MathF.PI / 180f) / 2f) * aspect);

    /// <summary>
    /// The camera's heading on the ground (yaw 0 faces −z, growing anticlockwise from above, as <c>Angles.Forward</c>) from
    /// its <paramref name="forward"/> (−basis Z); looking straight down, from its screen's <paramref name="up"/> instead.
    /// </summary>
    public static float CameraYaw(Vector3 forward, Vector3 up)
    {
        Vector3 along = forward.X * forward.X + forward.Z * forward.Z > StraightDown * StraightDown ? forward : up;
        return MathF.Atan2(-along.X, -along.Z);
    }
}
