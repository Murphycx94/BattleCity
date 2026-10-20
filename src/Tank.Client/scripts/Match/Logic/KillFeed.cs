namespace Tank.Client.Match;

/// <summary>
/// One line of the kill feed: who knocked out whom, each with the side they are on (for the colours). A shooter the
/// server hid from the player (id 0) has no name: the line then reads "victim 被击毁". <c>Friendly</c> is a kill within one side.
/// </summary>
public readonly record struct FeedLine(string? Killer, Side KillerSide, string Victim, Side VictimSide, bool Friendly, double ExpiresAt)
{
    public const string KilledVerb = "击毁", FriendlyVerb = "误伤", HiddenShooterTail = "被击毁";

    /// <summary>The whole line as text; allocates, so read it when the feed changed.</summary>
    public string Text => Killer is null
        ? $"{Victim} {HiddenShooterTail}"
        : $"{Killer} {(Friendly ? FriendlyVerb : KilledVerb)} {Victim}";
}

/// <summary>
/// The kill feed's list (blueprint §3.3, §4.2): the newest kills, at most <see cref="Capacity"/>, each gone
/// <see cref="Lifetime"/> seconds after it was added; the oldest goes when a sixth comes. Order is oldest first, newest
/// last. It lives in a fixed array, so adding and reading allocate nothing; <see cref="Revision"/> changes whenever the
/// visible lines do, so a view redraws only then.
/// </summary>
public sealed class KillFeed
{
    public const int Capacity = 5;
    public const double Lifetime = 5.0;   // s

    private readonly FeedLine[] _lines = new FeedLine[Capacity];
    private int _count;

    /// <summary>Changes whenever a line is added or has expired.</summary>
    public int Revision { get; private set; }

    /// <summary>Whether a kill between these sides is within one side (the player's own or an ally's, never an enemy's).</summary>
    public static bool IsFriendly(Side killer, Side victim) => killer != Side.Enemy && victim != Side.Enemy;

    /// <param name="killer">The shooter's name, or null when the shooter is unknown (id 0).</param>
    /// <param name="now">Seconds on the clock the caller reads <see cref="Visible"/> with.</param>
    public void Add(string? killer, Side killerSide, string victim, Side victimSide, bool friendly, double now)
    {
        if (_count == Capacity)
        {
            Array.Copy(_lines, 1, _lines, 0, Capacity - 1);
            _count--;
        }
        _lines[_count++] = new FeedLine(killer, killerSide, victim, victimSide, friendly, now + Lifetime);
        Revision++;
    }

    /// <summary>The lines still showing at <paramref name="now"/>, oldest first; valid until the next call.</summary>
    public ReadOnlySpan<FeedLine> Visible(double now)
    {
        int expired = 0;
        while (expired < _count && _lines[expired].ExpiresAt <= now) expired++;
        if (expired > 0)
        {
            Array.Copy(_lines, expired, _lines, 0, _count - expired);
            _count -= expired;
            Revision++;
        }
        return new ReadOnlySpan<FeedLine>(_lines, 0, _count);
    }
}
