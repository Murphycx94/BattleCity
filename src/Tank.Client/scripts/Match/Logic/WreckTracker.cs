namespace Tank.Client.Match;

/// <summary>
/// Decides when a tank's model turns into a wreck or back (blueprint §3.3, R5): the event is the word, the snapshot's
/// flag the fallback. A <c>TankDestroyed</c> or <c>TankRevived</c> event changes the look at once; a flag that has
/// disagreed with the look for longer than <see cref="FallbackSeconds"/> without an event (a tank first seen as a wreck,
/// a lost event, a respawn out of sight) changes it quietly, with no blast and no turret flight.
/// </summary>
public sealed class WreckTracker
{
    /// <summary>How long the snapshot may disagree with the look before it wins: the events run about 100 ms behind the state.</summary>
    public const float FallbackSeconds = 0.3f;

    private float _disagreed;   // seconds the state has disagreed with the look

    public enum Change : byte
    {
        None,
        /// <summary>The kill, seen: turret flight, charring, smoke and a blast.</summary>
        Wreck,
        /// <summary>A wreck already lying there: straight to the settled look, no blast.</summary>
        Settle,
        /// <summary>Back to an intact tank.</summary>
        Restore,
    }

    /// <summary>Whether the model is a wreck now.</summary>
    public bool Wrecked { get; private set; }

    /// <summary>A <c>TankDestroyed</c> event for this tank.</summary>
    public Change Destroyed()
    {
        _disagreed = 0f;
        if (Wrecked) return Change.None;
        Wrecked = true;
        return Change.Wreck;
    }

    /// <summary>A <c>TankRevived</c> event for this tank.</summary>
    public Change Revived()
    {
        _disagreed = 0f;
        if (!Wrecked) return Change.None;
        Wrecked = false;
        return Change.Restore;
    }

    /// <summary>
    /// The snapshot's flag this frame. A disagreement is tolerated for <see cref="FallbackSeconds"/> (the event is on its
    /// way), then the state wins; with <paramref name="firstSight"/> (the tank wasn't drawn the frame before: never seen,
    /// or back in sight) it wins at once.
    /// </summary>
    public Change Observe(bool destroyed, float dt, bool firstSight = false)
    {
        if (destroyed == Wrecked)
        {
            _disagreed = 0f;
            return Change.None;
        }
        _disagreed += dt;
        if (!firstSight && _disagreed <= FallbackSeconds) return Change.None;
        _disagreed = 0f;
        Wrecked = destroyed;
        return destroyed ? Change.Settle : Change.Restore;
    }
}
