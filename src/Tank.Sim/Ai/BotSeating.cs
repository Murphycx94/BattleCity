using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>
/// How a room is seated for a match (5C batch C review follow-up): the server's rule, here so that the tests which play
/// whole matches seat them as the server does. Every seat goes on the thinner team when it is taken (a player's when they
/// come into the room, a bot's at the match start), team 0 on a tie; the bot seats are filled in slot order, each drawing
/// its tank from the match seed (<see cref="NextTankIndex"/>); each bot gets its own seed from the match's and its slot
/// (<see cref="BotSeed"/>).
/// </summary>
/// <remarks>
/// The bot seeds are the match seed XORed with the slot times <see cref="SeedStride"/>, not hashed: two slots' seeds
/// differ in a fixed pattern of bits, the same in every match, so their random numbers aren't independent (a known
/// limitation, left as it is: the bots' draws haven't been seen to line up).
/// </remarks>
public sealed class BotSeating
{
    /// <summary>The stride between the bots' seeds: a prime, so each slot's seed differs from the match seed in its own way.</summary>
    public const int SeedStride = 7919;

    private XorShift _rng;

    public BotSeating(uint matchSeed)
    {
        MatchSeed = matchSeed;
        _rng = new XorShift(matchSeed);
    }

    public uint MatchSeed { get; }

    /// <summary>The tank (an index into <see cref="TankCatalog.All"/>) of the next bot seat filled, the seats taken in slot order.</summary>
    public int NextTankIndex() => (int)(_rng.NextUInt() % (uint)TankCatalog.All.Count);

    /// <summary>The seed of the bot in <paramref name="slot"/>.</summary>
    public uint BotSeed(int slot) => MatchSeed ^ (uint)(slot * SeedStride);

    /// <summary>The team a seat taken now goes on, with <paramref name="onTeam0"/> and <paramref name="onTeam1"/> seats taken already: the thinner, team 0 on a tie.</summary>
    public static byte ThinnerTeam(int onTeam0, int onTeam1) => (byte)(onTeam1 < onTeam0 ? 1 : 0);
}
