using Tank.Core.Math;

namespace Tank.Net.Client;

/// <summary>Input sampled by the presentation layer each frame.</summary>
/// <param name="Move">Current movement direction, null when no direction is held.</param>
/// <param name="FireHeld">Fire button is down.</param>
/// <param name="FirePressed">Fire went down at some point since the previous frame (never lose a quick tap).</param>
public readonly record struct LocalInput(Dir? Move, bool FireHeld, bool FirePressed)
{
    public static readonly LocalInput None = new(null, false, false);
}
