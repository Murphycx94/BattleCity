using SN = System.Numerics;
using Tank.Sim.Maps;

namespace Tank.Client.Match;

/// <summary>
/// What the camera does while the player's tank is a wreck (blueprint §5 T8). The kill close-up holds the wreck for
/// <see cref="CloseUpSeconds"/>; a mode with no respawn then follows the first living candidate on its own, a respawning
/// one stays on the wreck until the player steps away. Q, E and a click step through the candidates (live team-mates in a
/// mode with them, everyone alive in a free-for-all or royale — <see cref="SpectateCandidates.Fill"/>), wrapping at both
/// ends; F frees the camera where <see cref="SpectateCandidates.MayRoam"/> allows (free-for-all, royale, and duos once the
/// own team is out). A followed tank that dies is swapped for the next, and when nobody is left the camera goes free where
/// it may roam, back to the wreck in a team mode. Pure logic: the scene owns the camera and feeds the candidates (living
/// tank ids, in a stable order) each frame.
/// </summary>
public sealed class SpectateState
{
    /// <summary>How long the camera holds the fresh wreck before anything else.</summary>
    public const float CloseUpSeconds = 3f;

    /// <summary>The free camera's focus speed over the ground, m/s.</summary>
    public const float FocusSpeed = 60f;

    /// <summary>How much faster the focus moves while Shift is held.</summary>
    public const float FastMultiplier = 3f;

    public enum Mode : byte
    {
        /// <summary>The camera orbits the player's own wreck.</summary>
        Wreck,
        /// <summary>The camera orbits the tank <see cref="TargetId"/>.</summary>
        Follow,
        /// <summary>The camera orbits a focus the player drives over the ground.</summary>
        Free,
    }

    private float _closeUpLeft;
    private bool _autoFollow;

    public Mode Current { get; private set; }

    /// <summary>The tank the camera follows in <see cref="Mode.Follow"/>; 0 otherwise.</summary>
    public int TargetId { get; private set; }

    /// <summary>The own tank was just destroyed: the close-up begins. <paramref name="autoFollow"/> hands the camera on once it ends (modes with no respawn).</summary>
    public void OnDeath(bool autoFollow)
    {
        Current = Mode.Wreck;
        TargetId = 0;
        _closeUpLeft = CloseUpSeconds;
        _autoFollow = autoFollow;
    }

    /// <summary>
    /// Q (−1), E or a click (+1): the previous or next of <paramref name="candidates"/>, wrapping at both ends. From the
    /// wreck or the free camera, the first (stepping on) or last (stepping back). No candidates, no change.
    /// </summary>
    public void Step(int step, ReadOnlySpan<int> candidates)
    {
        if (step == 0 || candidates.IsEmpty) return;
        int index = candidates.IndexOf(TargetId);
        int count = candidates.Length;
        int next = index < 0
            ? (step > 0 ? 0 : count - 1)
            : ((index + step) % count + count) % count;
        TargetId = candidates[next];
        Current = Mode.Follow;
    }

    /// <summary>
    /// F where <see cref="SpectateCandidates.MayRoam"/> allows it (free-for-all, royale, duos once the own team is out): the
    /// free camera (the scene seeds the focus where the view was).
    /// </summary>
    public void GoFree()
    {
        Current = Mode.Free;
        TargetId = 0;
    }

    /// <summary>
    /// Once per frame while dead. The close-up runs down and, if it is to hand over, follows the first candidate; a
    /// followed tank no longer among <paramref name="candidates"/> is swapped for the next. With no candidates at all,
    /// <paramref name="freeWhenNone"/> (<see cref="SpectateCandidates.MayRoam"/>) goes to the free camera, otherwise back to the wreck.
    /// </summary>
    public void Update(float dt, ReadOnlySpan<int> candidates, bool freeWhenNone)
    {
        switch (Current)
        {
            case Mode.Wreck:
                _closeUpLeft = Math.Max(0f, _closeUpLeft - dt);
                if (_autoFollow && _closeUpLeft <= 0f) FollowOrFallBack(candidates, freeWhenNone);
                break;
            case Mode.Follow when candidates.IndexOf(TargetId) < 0:
                FollowOrFallBack(candidates, freeWhenNone);
                break;
        }
    }

    private void FollowOrFallBack(ReadOnlySpan<int> candidates, bool freeWhenNone)
    {
        if (!candidates.IsEmpty)
        {
            TargetId = candidates[0];
            Current = Mode.Follow;
            return;
        }
        TargetId = 0;
        Current = freeWhenNone ? Mode.Free : Mode.Wreck;
    }

    /// <summary>
    /// The free camera's focus moved over the XZ plane, relative to the camera's yaw (yaw 0 faces −Z): <paramref name="forward"/>
    /// is W−S, <paramref name="right"/> D−A, <paramref name="fast"/> Shift; a diagonal is no faster. The result is
    /// clamped to the field's playable area; the scene reads the height off the terrain.
    /// </summary>
    public static SN.Vector2 MoveFocus(SN.Vector2 focus, float cameraYaw, int forward, int right, bool fast, float dt, Battlefield field)
    {
        var direction = new SN.Vector2(
            -MathF.Sin(cameraYaw) * forward + MathF.Cos(cameraYaw) * right,
            -MathF.Cos(cameraYaw) * forward - MathF.Sin(cameraYaw) * right);
        if (direction.LengthSquared() > 0f)
            focus += SN.Vector2.Normalize(direction) * (FocusSpeed * (fast ? FastMultiplier : 1f) * dt);
        return field.ClampToPlayable(focus);
    }
}
