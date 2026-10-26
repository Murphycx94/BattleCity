using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>What <see cref="PropDown.See"/> found: nothing new, the prop just went down, or it stood up again.</summary>
public enum PropChange : byte { None, Fell, Rose }

/// <summary>
/// One prop that goes down over <see cref="Duration"/> seconds as a view shows it: a tree falling, a house coming down, a
/// fence toppling (<c>ForestViews</c>, <c>HouseViews</c>, <c>WallViews</c>). The battlefield says
/// whether it stands (<see cref="See"/>); its <c>PropDestroyed</c> event says which way it was pushed (<see cref="Pushed"/>).
/// In the test drive the event comes first, so the prop goes down the pushed way from the start. In a match the server's
/// prop state is applied as it arrives and its events only when the render clock reaches them, so the push comes once
/// the prop is already on its way down (or there): <see cref="Pushed"/> then says so, and the view aims it again. The
/// first look after a view is built (a mid-match join, or a scene built after props fell) puts a prop already down
/// straight at its end, with no fall.
/// </summary>
public struct PropDown
{
    public PropDown(float duration) => (Duration, Standing, Elapsed, Push) = (duration, true, -1f, null);

    /// <summary>How long it takes to go down, seconds.</summary>
    public float Duration { get; }

    public bool Standing { get; private set; }

    /// <summary>Seconds since it went down (<see cref="Duration"/> or more once there); −1 while it stands.</summary>
    public float Elapsed { get; private set; }

    /// <summary>The level direction it was last pushed down, if one came; forgotten when it stands up again.</summary>
    public SN.Vector2? Push { get; private set; }

    /// <summary>On its way down.</summary>
    public readonly bool Moving => !Standing && Elapsed < Duration;

    /// <summary>Down and come to rest.</summary>
    public readonly bool AtRest => !Standing && Elapsed >= Duration;

    /// <summary>
    /// Takes the battlefield's word for whether it stands. <see cref="PropChange.Fell"/>: it just went down, starting its
    /// way down, or (<paramref name="snap"/>, the view's first look) already at its end; <see cref="PropChange.Rose"/>:
    /// it stands again, its push forgotten.
    /// </summary>
    public PropChange See(bool standing, bool snap)
    {
        if (standing == Standing) return PropChange.None;
        Standing = standing;
        if (standing)
        {
            (Elapsed, Push) = (-1f, null);
            return PropChange.Rose;
        }
        Elapsed = snap ? Duration : 0f;
        return PropChange.Fell;
    }

    /// <summary>Notes the way it was pushed down; true when it is already down, so the view must aim it that way now.</summary>
    public bool Pushed(SN.Vector2 push)
    {
        Push = push;
        return !Standing;
    }

    /// <summary>Moves it on by <paramref name="delta"/> seconds; true once it has come to rest.</summary>
    public bool Step(float delta)
    {
        Elapsed += delta;
        return Elapsed >= Duration;
    }
}
