namespace Tank.Client.Driving;

/// <summary>
/// The fire button as the sim wants it: a press shorter than a tick must still shoot once, and a held button must keep
/// shooting. <see cref="Peek"/> reads without consuming, so a frame that runs no tick can look at it and leave the click
/// for the frame that does; <see cref="EndTick"/> is called once a tick has taken it.
/// </summary>
public sealed class FireLatch
{
    private bool _latched;
    private bool _held;

    /// <summary>The button went down: latched until a tick has seen it, held until it comes up.</summary>
    public void Press() => _latched = _held = true;

    /// <summary>The button came up. A press not yet taken by a tick still counts.</summary>
    public void Release() => _held = false;

    /// <summary>True while held, or when a press has not been taken by a tick yet. Does not clear anything.</summary>
    public bool Peek() => _latched || _held;

    /// <summary>A tick has taken the fire state: drop the latched press (a held button stays down).</summary>
    public void EndTick() => _latched = false;

    /// <summary>Forgets everything, held or not (focus lost, so a release may never arrive).</summary>
    public void Clear() => _latched = _held = false;
}
