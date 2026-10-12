using Godot;
using Tank.Core.Math;
using Tank.Net.Client;

namespace Tank.Client.Controls;

/// <summary>
/// Reads keyboard/gamepad. When several directions are held, the most recently pressed one wins
/// (same as the original game), so rolling from one key to the next never stalls the tank.
/// </summary>
public sealed class InputSampler
{
    private static readonly (Dir Dir, string Action)[] Directions =
    {
        (Dir.Up, "move_up"), (Dir.Down, "move_down"), (Dir.Left, "move_left"), (Dir.Right, "move_right"),
    };

    private readonly List<Dir> _held = new(4);
    private bool _firePressedSinceSample;

    /// <summary>Call from _Input so taps shorter than a frame are never missed.</summary>
    public void OnInputEvent(InputEvent e)
    {
        if (e.IsActionPressed("fire")) _firePressedSinceSample = true;
    }

    public LocalInput Sample()
    {
        foreach (var (dir, action) in Directions)
        {
            bool down = Godot.Input.IsActionPressed(action);
            if (down && !_held.Contains(dir)) _held.Add(dir);
            else if (!down) _held.Remove(dir);
        }
        Dir? move = _held.Count > 0 ? _held[^1] : null;
        bool held = Godot.Input.IsActionPressed("fire");
        bool pressed = _firePressedSinceSample || Godot.Input.IsActionJustPressed("fire");
        _firePressedSinceSample = false;
        return new LocalInput(move, held, pressed);
    }
}
