using Tank.Sim.Modes;

namespace Tank.Sim.Tests;

/// <summary>Which team a person and a bot go on in a duos room (stage 6 design §5.1).</summary>
public sealed class DuoSeatingTests
{
    private const byte NoTeam = MatchRules.NoTeam;

    /// <summary>Eight team counts: <paramref name="first"/> as given, the rest <paramref name="rest"/>.</summary>
    private static int[] Counts(int rest, params int[] first)
    {
        var counts = Enumerable.Repeat(rest, ModeRules.DuoTeams).ToArray();
        first.CopyTo(counts, 0);
        return counts;
    }

    [Fact]
    public void A_person_takes_the_lowest_empty_team_then_the_lowest_with_one_seat()
    {
        Assert.Equal(0, DuoSeating.ForPerson(Counts(0)));
        Assert.Equal(1, DuoSeating.ForPerson(Counts(0, 1)));
        Assert.Equal(0, DuoSeating.ForPerson(Counts(1)));
        Assert.Equal(1, DuoSeating.ForPerson(Counts(1, 2)));
        Assert.Equal(NoTeam, DuoSeating.ForPerson(Counts(2)));
        Assert.Equal(1, DuoSeating.ForPerson(Counts(2, 2, 0)));
    }

    [Fact]
    public void A_bot_takes_the_lowest_team_with_one_seat_then_the_lowest_empty()
    {
        Assert.Equal(0, DuoSeating.ForBot(Counts(0)));
        Assert.Equal(0, DuoSeating.ForBot(Counts(0, 1)));
        Assert.Equal(2, DuoSeating.ForBot(Counts(0, 0, 0, 1)));
        Assert.Equal(1, DuoSeating.ForBot(Counts(0, 2, 1, 0)));
        Assert.Equal(2, DuoSeating.ForBot(Counts(0, 2, 2, 0)));
        Assert.Equal(NoTeam, DuoSeating.ForBot(Counts(2)));
    }

    /// <summary>Seats <paramref name="bots"/> bots one by one on <paramref name="counts"/>, as the server does at the start; their teams in order.</summary>
    private static byte[] SeatBots(int[] counts, int bots)
    {
        var teams = new byte[bots];
        for (int i = 0; i < bots; i++)
        {
            teams[i] = DuoSeating.ForBot(counts);
            Assert.NotEqual(NoTeam, teams[i]);
            counts[teams[i]]++;
        }
        return teams;
    }

    [Fact]
    public void One_person_and_fifteen_bots_make_eight_full_teams()
    {
        int[] counts = Counts(0, 1);
        byte[] bots = SeatBots(counts, 15);

        Assert.Equal(0, bots[0]);
        Assert.All(counts, n => Assert.Equal(2, n));
    }

    [Fact]
    public void Three_people_on_three_teams_and_thirteen_bots_make_eight_full_teams()
    {
        int[] counts = Counts(0, 1, 1, 1);
        byte[] bots = SeatBots(counts, 13);

        Assert.Equal(new byte[] { 0, 1, 2 }, bots[..3]);
        Assert.All(counts, n => Assert.Equal(2, n));
    }

    [Fact]
    public void Two_people_together_leave_their_team_to_themselves_and_the_bots_pair_up_on_the_rest()
    {
        int[] counts = Counts(0, 0, 0, 0, 2);
        byte[] bots = SeatBots(counts, 14);

        Assert.Equal(new byte[] { 0, 0, 1, 1, 2, 2, 4, 4, 5, 5, 6, 6, 7, 7 }, bots);
        Assert.All(counts, n => Assert.Equal(2, n));
    }

    [Fact]
    public void A_seat_may_move_to_a_team_with_room_or_stay_on_its_own_full_one()
    {
        int[] counts = Counts(0, 2, 1);

        Assert.False(DuoSeating.HasRoom(counts, 0, NoTeam));
        Assert.False(DuoSeating.HasRoom(counts, 0, 1));
        Assert.True(DuoSeating.HasRoom(counts, 0, 0));
        Assert.True(DuoSeating.HasRoom(counts, 1, NoTeam));
        Assert.True(DuoSeating.HasRoom(counts, 1, 0));
        Assert.True(DuoSeating.HasRoom(counts, 1, 1));
        Assert.True(DuoSeating.HasRoom(counts, 7, 0));
        Assert.False(DuoSeating.HasRoom(counts, 8, NoTeam));
        Assert.False(DuoSeating.HasRoom(counts, NoTeam, NoTeam));
    }

    [Fact]
    public void Manned_counts_the_teams_with_anyone_on()
    {
        Assert.Equal(0, DuoSeating.Manned(Counts(0)));
        Assert.Equal(1, DuoSeating.Manned(Counts(0, 2)));
        Assert.Equal(2, DuoSeating.Manned(Counts(0, 0, 1, 0, 0, 0, 0, 0, 2)));
        Assert.Equal(8, DuoSeating.Manned(Counts(1)));
    }

    [Fact]
    public void Counts_for_other_than_eight_teams_are_refused()
    {
        int[] seven = new int[7], nine = new int[9];

        Assert.Throws<ArgumentException>("counts", () => DuoSeating.ForPerson(seven));
        Assert.Throws<ArgumentException>("counts", () => DuoSeating.ForBot(nine));
        Assert.Throws<ArgumentException>("counts", () => DuoSeating.HasRoom(seven, 0, NoTeam));
        Assert.Throws<ArgumentException>("counts", () => DuoSeating.Manned(ReadOnlySpan<int>.Empty));
    }
}
