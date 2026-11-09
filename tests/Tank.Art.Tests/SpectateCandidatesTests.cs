using System.Numerics;
using Tank.Client.Match;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class SpectateCandidatesTests
{
    private static RemoteTank Tank(int id, byte team, bool destroyed = false) =>
        new(new BattleEntity { Id = id, Team = team, Flags = destroyed ? TankFlags.Destroyed : TankFlags.None }, Vector2.Zero, 0f, 0f, 0f, 0f, false);

    private static List<int> Candidates(BattleMode mode, byte ownTeam, IReadOnlyList<RemoteTank> others)
    {
        var into = new List<int> { 99 };   // whatever was there is cleared
        SpectateCandidates.Fill(mode, ownTeam, others, into);
        return into;
    }

    [Fact]
    public void In_duos_a_living_team_mate_is_the_only_one_to_watch_and_the_camera_may_not_roam()
    {
        RemoteTank[] others = { Tank(2, 4), Tank(5, 3), Tank(7, 1, destroyed: true), Tank(9, 6) };

        Assert.Equal(new[] { 5 }, Candidates(BattleMode.Duos, 3, others));
        Assert.False(SpectateCandidates.MayRoam(BattleMode.Duos, 3, others));
    }

    [Fact]
    public void In_duos_once_the_team_mate_is_down_every_living_tank_may_be_watched_and_the_camera_may_roam()
    {
        RemoteTank[] others = { Tank(2, 4), Tank(5, 3, destroyed: true), Tank(7, 1, destroyed: true), Tank(9, 6) };

        Assert.Equal(new[] { 2, 9 }, Candidates(BattleMode.Duos, 3, others));
        Assert.True(SpectateCandidates.MayRoam(BattleMode.Duos, 3, others));
    }

    [Fact]
    public void In_duos_with_nobody_left_there_is_nothing_to_watch_and_the_camera_may_roam()
    {
        RemoteTank[] others = { Tank(5, 3, destroyed: true) };

        Assert.Empty(Candidates(BattleMode.Duos, 3, others));
        Assert.True(SpectateCandidates.MayRoam(BattleMode.Duos, 3, Array.Empty<RemoteTank>()));
    }

    [Theory]
    [InlineData(BattleMode.Royale)]
    [InlineData(BattleMode.FreeForAll)]
    public void In_a_royale_or_a_free_for_all_every_living_tank_may_be_watched_and_the_camera_may_roam(BattleMode mode)
    {
        RemoteTank[] others = { Tank(2, 2), Tank(3, 3, destroyed: true), Tank(4, 0), Tank(5, 5) };   // team 0 is the player's own number: no team-mate for all that

        Assert.Equal(new[] { 2, 4, 5 }, Candidates(mode, 0, others));
        Assert.True(SpectateCandidates.MayRoam(mode, 0, others));
    }

    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    [InlineData(BattleMode.Standard)]
    public void In_a_team_mode_only_living_team_mates_and_never_roaming_even_with_them_all_down(BattleMode mode)
    {
        RemoteTank[] others = { Tank(2, 1), Tank(3, 0), Tank(4, 0, destroyed: true), Tank(5, 1), Tank(6, 0) };

        Assert.Equal(new[] { 3, 6 }, Candidates(mode, 0, others));
        Assert.False(SpectateCandidates.MayRoam(mode, 0, others));
        Assert.Empty(Candidates(mode, 0, new[] { Tank(2, 1), Tank(3, 0, destroyed: true) }));
        Assert.False(SpectateCandidates.MayRoam(mode, 0, new[] { Tank(2, 1), Tank(3, 0, destroyed: true) }));
    }

    [Fact]
    public void Filling_and_asking_allocate_nothing_once_the_list_has_room()
    {
        Allocation.AssertNone(() =>
        {
            RemoteTank[] others = { Tank(2, 4), Tank(5, 3), Tank(7, 1, destroyed: true), Tank(9, 6) };
            IReadOnlyList<RemoteTank> list = others;
            var into = new List<int>(16);
            SpectateCandidates.Fill(BattleMode.Duos, 3, list, into);
            SpectateCandidates.MayRoam(BattleMode.Duos, 3, list);

            Allocation.Window window = Allocation.Start();
            bool roam = false;
            for (int i = 0; i < 100; i++)
            {
                SpectateCandidates.Fill(BattleMode.Duos, 3, list, into);
                SpectateCandidates.Fill(BattleMode.Royale, 3, list, into);
                roam = SpectateCandidates.MayRoam(BattleMode.Duos, 3, list);
            }
            Allocation.Measured measured = window.Stop();

            Assert.False(roam);
            return measured;
        });
    }
}
