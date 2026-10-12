using Tank.Core.Math;
using Tank.Net.Client;

namespace Tank.Client.Bootstrap;

/// <summary>Very simple in-process bot players for solo testing: wander and shoot.</summary>
public static class BotDriver
{
    public static LocalInput Think(int bot, double t)
    {
        int phase = (int)(t / (0.45 + bot * 0.07)) + bot * 3;
        Dir? dir = (phase % 7) switch
        {
            0 or 4 => Dir.Up,
            1 => Dir.Left,
            2 => Dir.Down,
            3 or 5 => Dir.Right,
            _ => null,
        };
        bool fire = ((int)(t * 5) + bot) % 4 == 0;
        return new LocalInput(dir, fire, fire);
    }
}
