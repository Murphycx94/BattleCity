namespace Tank.Sim.Modes;

/// <summary>
/// How a duos room is teamed (stage 6 design §5.1), the server's rule and the client's mirror of it. <c>counts</c> is the
/// seats on each team now (length <see cref="ModeRules.DuoTeams"/>, else <see cref="ArgumentException"/>). No state; no allocation.
/// </summary>
public static class DuoSeating
{
    /// <summary>
    /// A person coming into the room (or re-teamed when the room switches to duos, in slot order): the lowest-numbered empty
    /// team; none empty, the lowest with one seat; all full, <see cref="MatchRules.NoTeam"/>.
    /// </summary>
    public static byte ForPerson(ReadOnlySpan<int> counts)
    {
        Check(counts);
        byte team = Lowest(counts, 0);
        return team != MatchRules.NoTeam ? team : Lowest(counts, 1);
    }

    /// <summary>
    /// A bot seated at the match start (bot seats are filled in slot order): the lowest-numbered team with exactly one seat, so
    /// people who came alone get a partner first; none, the lowest empty one; all full, <see cref="MatchRules.NoTeam"/>.
    /// </summary>
    public static byte ForBot(ReadOnlySpan<int> counts)
    {
        Check(counts);
        byte team = Lowest(counts, 1);
        return team != MatchRules.NoTeam ? team : Lowest(counts, 0);
    }

    /// <summary>
    /// Whether a seat may move to <paramref name="team"/>: 0–7 with fewer than <see cref="ModeRules.DuoTeamSize"/> seats on it, not
    /// counting the mover (<paramref name="current"/>: its team now, or <see cref="MatchRules.NoTeam"/>).
    /// </summary>
    public static bool HasRoom(ReadOnlySpan<int> counts, byte team, byte current)
    {
        Check(counts);
        if (team >= ModeRules.DuoTeams) return false;
        int others = counts[team] - (team == current ? 1 : 0);
        return others < ModeRules.DuoTeamSize;
    }

    /// <summary>The teams with at least one seat in <paramref name="counts"/>.</summary>
    public static int Manned(ReadOnlySpan<int> counts)
    {
        Check(counts);
        int manned = 0;
        foreach (int n in counts) if (n > 0) manned++;
        return manned;
    }

    /// <summary>The lowest-numbered team with exactly <paramref name="seats"/> seats, or <see cref="MatchRules.NoTeam"/>.</summary>
    private static byte Lowest(ReadOnlySpan<int> counts, int seats)
    {
        for (int team = 0; team < counts.Length; team++)
            if (counts[team] == seats) return (byte)team;
        return MatchRules.NoTeam;
    }

    private static void Check(ReadOnlySpan<int> counts)
    {
        if (counts.Length != ModeRules.DuoTeams)
            throw new ArgumentException($"Duos has {ModeRules.DuoTeams} teams, got counts for {counts.Length}.", nameof(counts));
    }
}
