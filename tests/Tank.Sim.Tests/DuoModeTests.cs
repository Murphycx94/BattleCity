using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Sim.Tests;

/// <summary>Duos as a mode: its value, its id and its numbers (stage 6 design §5).</summary>
public sealed class DuoModeTests
{
    private const int Tps = SimConstants.TickRate;

    [Fact]
    public void Duos_is_the_sixth_and_last_mode()
    {
        Assert.Equal(5, (byte)BattleMode.Duos);
        Assert.Equal(BattleMode.Duos, ModeRules.LastMode);
        Assert.Equal(6, ModeRules.All.Count);
        Assert.Equal(BattleMode.Duos, ModeRules.All[^1]);
        Assert.Equal(
            new[] { BattleMode.TeamBattle, BattleMode.Annihilation, BattleMode.FreeForAll, BattleMode.Standard, BattleMode.Royale, BattleMode.Duos },
            ModeRules.All);
    }

    [Fact]
    public void Duos_is_called_duos_on_the_command_line()
    {
        Assert.Equal("duos", ModeRules.Id(BattleMode.Duos));
        Assert.True(ModeRules.TryParse("DUOS", out BattleMode duos));
        Assert.Equal(BattleMode.Duos, duos);
    }

    [Fact]
    public void Duos_seats_sixteen_in_eight_teams_of_two()
    {
        Assert.Equal(16, ModeRules.MaxPlayersFor(BattleMode.Duos));
        Assert.Equal(15, ModeRules.MaxBotsFor(BattleMode.Duos));
        Assert.Equal(8, ModeRules.DuoTeams);
        Assert.Equal(2, ModeRules.DuoTeamSize);
        Assert.Equal(ModeRules.DuoTeams * ModeRules.DuoTeamSize, ModeRules.MaxPlayersFor(BattleMode.Duos));
    }

    [Fact]
    public void Duos_has_the_zone_and_the_crates_but_no_two_teams_respawns_late_joins_or_bases()
    {
        Assert.True(ModeRules.HasZone(BattleMode.Duos));
        Assert.True(ModeRules.HasCrates(BattleMode.Duos));
        Assert.False(ModeRules.HasTeams(BattleMode.Duos));
        Assert.False(ModeRules.Respawns(BattleMode.Duos));
        Assert.False(ModeRules.JoinsMidMatch(BattleMode.Duos));
        Assert.False(ModeRules.HasBases(BattleMode.Duos));
        Assert.True(ModeRules.IsDuos(BattleMode.Duos));
        foreach (BattleMode mode in ModeRules.All.Where(m => m != BattleMode.Duos)) Assert.False(ModeRules.IsDuos(mode));
    }

    [Fact]
    public void The_two_team_modes_and_duos_have_allies_the_free_for_all_and_the_royale_dont()
    {
        Assert.Equal(new[] { true, true, false, true, false, true }, ModeRules.All.Select(ModeRules.HasAllies));
        Assert.Equal(new[] { true, true, false, true, false, false }, ModeRules.All.Select(ModeRules.HasTeams));
    }

    [Fact]
    public void A_seat_picks_from_two_teams_in_the_two_team_modes_eight_in_duos_and_none_elsewhere()
    {
        Assert.Equal(new[] { 2, 2, 0, 2, 0, 8 }, ModeRules.All.Select(ModeRules.TeamsFor));
    }

    [Fact]
    public void Duos_is_played_on_the_riverland_only_for_ten_minutes()
    {
        Assert.Equal("riverland", ModeRules.OnlyMap(BattleMode.Duos));
        Assert.Equal(RiverlandMap.Id, ModeRules.OnlyMap(BattleMode.Duos));
        Assert.True(ModeRules.AllowsMap(BattleMode.Duos, "Riverland"));
        Assert.False(ModeRules.AllowsMap(BattleMode.Duos, "sandbox"));
        Assert.Equal(10 * 60 * Tps, ModeRules.DurationTicks(BattleMode.Duos));
        Assert.Equal(ModeRules.DurationTicks(BattleMode.Royale), ModeRules.DurationTicks(BattleMode.Duos));
    }
}
