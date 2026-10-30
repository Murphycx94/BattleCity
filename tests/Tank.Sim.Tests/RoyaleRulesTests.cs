using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>The royale's mode rules, its 16 seats, its seeded spawn order and its solo ranking (stage 6 design §2).</summary>
public sealed class RoyaleRulesTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>Sixteen solo points in a 4 × 4 grid 40 m apart; the tests that fight move the tanks where they want them.</summary>
    private static readonly SpawnLayout Grid = new(
        Team: Array.Empty<SpawnPoint>(),
        Solo: Enumerable.Range(0, 16).Select(i => new SpawnPoint(new Vector2(30 + 40 * (i % 4), 30 + 40 * (i / 4)), 0f, 0)).ToArray());

    /// <summary>Flat ground the size of the riverland, so its spawn points aren't clamped onto a small field.</summary>
    private static Battlefield RiverlandSizedFlat() =>
        TestWorld.Field(new HeightField(301, 301, 4f, new float[301 * 301]));

    private sealed class Match
    {
        public readonly Simulation Sim;
        public readonly MatchRules Rules;
        private readonly Dictionary<int, InputCmd> _orders = new();

        /// <param name="ticksBefore">Steps the simulation takes before the match starts, so the start tick isn't 0.</param>
        public Match(BattleMode mode = BattleMode.Royale, SpawnLayout? spawns = null, int? durationTicks = null, uint seed = 0, int ticksBefore = 0,
            Battlefield? field = null)
        {
            Sim = new Simulation(field ?? TestWorld.FlatField());
            for (int i = 0; i < ticksBefore; i++) Sim.Step(ReadOnlySpan<InputCmd>.Empty);
            Rules = new MatchRules(mode, Sim, spawns ?? Grid, durationTicks, seed: seed);
        }

        public int Join(byte slot) => Rules.Join(slot, TankCatalog.Medium, 0);

        public void Order(int tankId, InputCmd cmd) => _orders[tankId] = cmd;

        public void Hold(int tankId) => Order(tankId, new InputCmd(0, 0, 0, Tank(tankId).Yaw, 0f, false));

        public void Run(int ticks)
        {
            var inputs = new InputCmd[Simulation.MaxTanks].AsSpan();
            for (int i = 0; i < ticks; i++)
            {
                inputs = inputs[..Sim.TankCount];
                for (int k = 0; k < inputs.Length; k++)
                {
                    TankState t = Sim.Tanks[k];
                    inputs[k] = _orders.TryGetValue(t.Id, out InputCmd cmd) ? cmd : new InputCmd(0, 0, 0, t.Yaw, 0f, false);
                }
                Sim.Step(inputs);
                Rules.Update();
            }
        }

        /// <summary>Lays <paramref name="shooter"/>'s gun on <paramref name="target"/>'s hull front and keeps firing.</summary>
        public void Attack(int shooter, int target)
        {
            TankState s = Tank(shooter), t = Tank(target);
            var point = new Vector3(t.Position.X, 1.02f, t.Position.Y) + Vector3.Transform(new Vector3(0, 0, -3.1f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, t.Yaw));
            var (yaw, pitch) = Angles.AimAngles(point - TankPose.Compute(s, Sim.Field.Terrain).Trunnion(s.Spec));
            Order(shooter, new InputCmd(0, 0, 0, yaw, pitch, true));
        }

        public TankState Tank(int id) => Sim.Tanks[Sim.SlotOf(id)];

        public void Weaken(int id, int health) => Sim.TankRef(id).Health = health;

        public void Place(int id, float x, float z, float yaw)
        {
            Sim.TankRef(id).Position = new Vector2(x, z);
            Sim.TankRef(id).Yaw = yaw;
        }

        /// <summary>The spawn point each of the first <paramref name="seats"/> seats got.</summary>
        public Vector2[] Seat(int seats) => Enumerable.Range(0, seats).Select(slot => Tank(Join((byte)slot)).Position).ToArray();
    }

    [Fact]
    public void The_royale_is_the_fifth_mode_with_its_own_numbers()
    {
        Assert.Equal(4, (byte)BattleMode.Royale);
        Assert.Equal(new[] { BattleMode.TeamBattle, BattleMode.Annihilation, BattleMode.FreeForAll, BattleMode.Standard, BattleMode.Royale }, ModeRules.All);
        Assert.Equal("royale", ModeRules.Id(BattleMode.Royale));
        Assert.True(ModeRules.TryParse("ROYALE", out BattleMode royale) && royale == BattleMode.Royale);
        Assert.Equal(BattleMode.Royale, ModeRules.LastMode);
        Assert.Equal(18000, ModeRules.DurationTicks(BattleMode.Royale));
        Assert.Equal(10 * 60 * Tps, ModeRules.DurationTicks(BattleMode.Royale));
        Assert.False(ModeRules.HasTeams(BattleMode.Royale));
        Assert.False(ModeRules.Respawns(BattleMode.Royale));
        Assert.False(ModeRules.JoinsMidMatch(BattleMode.Royale));
        Assert.False(ModeRules.HasBases(BattleMode.Royale));
        foreach (BattleMode mode in ModeRules.All) Assert.Equal(mode == BattleMode.Royale, ModeRules.HasZone(mode));
    }

    [Fact]
    public void The_royale_seats_sixteen_and_the_other_modes_ten()
    {
        Assert.Equal(16, ModeRules.MaxPlayers);
        Assert.Equal(16, ModeRules.RoyaleMaxPlayers);
        Assert.Equal(10, ModeRules.DefaultMaxPlayers);
        Assert.Equal(16, ModeRules.MaxPlayersFor(BattleMode.Royale));
        Assert.Equal(15, ModeRules.MaxBotsFor(BattleMode.Royale));
        foreach (BattleMode mode in ModeRules.All.Where(m => m != BattleMode.Royale))
        {
            Assert.Equal(10, ModeRules.MaxPlayersFor(mode));
            Assert.Equal(9, ModeRules.MaxBotsFor(mode));
        }
        Assert.All(ModeRules.All, mode => Assert.InRange(ModeRules.MaxPlayersFor(mode), ModeRules.MinPlayers, ModeRules.MaxPlayers));
    }

    [Fact]
    public void The_royale_is_played_on_the_riverland_only()
    {
        Assert.Equal("riverland", ModeRules.OnlyMap(BattleMode.Royale));
        Assert.Equal(RiverlandMap.Id, ModeRules.OnlyMap(BattleMode.Royale));
        foreach (BattleMode mode in ModeRules.All.Where(m => m != BattleMode.Royale)) Assert.Null(ModeRules.OnlyMap(mode));
        Assert.True(ModeRules.AllowsMap(BattleMode.Royale, "Riverland"));
        Assert.True(ModeRules.AllowsMap(BattleMode.Royale, "riverland"));
        Assert.False(ModeRules.AllowsMap(BattleMode.Royale, "sandbox"));
        Assert.True(ModeRules.AllowsMap(BattleMode.Standard, "sandbox"));
        Assert.True(ModeRules.AllowsMap(BattleMode.FreeForAll, "riverland"));
    }

    [Fact]
    public void Sixteen_players_land_on_sixteen_different_riverland_solo_points_each_their_own_team()
    {
        var m = new Match(spawns: MapCatalog.Riverland.Spawns, seed: 11, field: RiverlandSizedFlat());
        for (byte slot = 0; slot < 16; slot++)
        {
            int id = m.Join(slot);
            Assert.Equal(slot, m.Tank(id).Team);
            Assert.Equal(slot, m.Rules.Of(slot).Team);
        }
        Assert.Equal(16, m.Sim.TankCount);
        Assert.Equal(16, m.Rules.Entrants);
        Vector2[] at = m.Sim.Tanks.ToArray().Select(t => t.Position).ToArray();
        Assert.Equal(16, at.Distinct().Count());
        Assert.All(at, p => Assert.Contains(p, RiverlandMap.Spawns.Solo.Select(s => s.Position)));
    }

    [Fact]
    public void The_royale_spawn_order_comes_from_the_match_seed()
    {
        Assert.Equal(new Match(seed: 7).Seat(6), new Match(seed: 7).Seat(6));
        var firsts = Enumerable.Range(1, 20).Select(seed => new Match(seed: (uint)seed).Seat(1)[0]).Distinct().Count();
        Assert.True(firsts >= 8, $"seat 0 took only {firsts} different points over 20 seeds");
        Assert.NotEqual(new Match(seed: 1).Seat(16), Grid.Solo.Select(p => p.Position).ToArray());
        Assert.Equal(0x5A17_0C5Eu, MatchRules.SpawnSalt);
    }

    [Fact]
    public void A_free_for_all_still_takes_its_points_in_list_order_whatever_the_seed()
    {
        foreach (uint seed in new uint[] { 0, 1, 7, 12345 })
        {
            var m = new Match(BattleMode.FreeForAll, seed: seed);
            Assert.Equal(Grid.Solo.Take(4).Select(p => p.Position), m.Seat(4));
        }
    }

    [Fact]
    public void A_royale_without_solo_points_throws_on_the_first_join()
    {
        var m = new Match(spawns: new SpawnLayout(Grid.Solo, Array.Empty<SpawnPoint>()), seed: 3);
        Assert.Throws<InvalidOperationException>(() => m.Join(0));
    }

    [Fact]
    public void A_royale_ends_with_the_last_one_standing_and_ranks_the_fallen_by_when_they_fell()
    {
        var m = new Match(seed: 5, ticksBefore: 17);
        int a = m.Join(0), b = m.Join(1), c = m.Join(2);
        Assert.Equal(17, m.Rules.StartTick);
        Assert.Equal(3, m.Rules.Entrants);
        m.Place(a, 100, 150, 0f);
        m.Place(b, 100, 110, MathF.PI);
        m.Place(c, 140, 110, MathF.PI);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(b, 1);
        m.Attack(a, b);
        m.Run(2 * Tps);
        Assert.True(m.Tank(b).IsDestroyed);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        int bFell = m.Rules.Of(1).EliminatedAt;
        Assert.Equal(bFell - 17, m.Rules.LastedTicks(1));
        Assert.Equal(m.Sim.Tick - 17, m.Rules.LastedTicks(0));   // still in: up to now
        m.Weaken(c, 1);
        m.Attack(a, c);
        m.Run(6 * Tps);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchEndReason.Eliminated, m.Rules.EndReason);
        Assert.Equal(0, m.Rules.WinnerSlot);
        Assert.Equal(MatchRules.NoTeam, m.Rules.WinnerTeam);
        Assert.Equal((1, 2, 3), (m.Rules.Of(0).Rank, m.Rules.Of(2).Rank, m.Rules.Of(1).Rank));
        Assert.Equal(bFell - 17, m.Rules.LastedTicks(1));
        Assert.Equal(m.Rules.Of(2).EliminatedAt - 17, m.Rules.LastedTicks(2));
        Assert.Equal(m.Rules.EndedAt - 17, m.Rules.LastedTicks(0));   // lasted to the end
        m.Run(Tps);
        Assert.Equal(m.Rules.EndedAt - 17, m.Rules.LastedTicks(0));   // the clock stops at the end
        Assert.Equal(0, m.Rules.LastedTicks(3));   // never joined
        Assert.Equal(0, m.Rules.LastedTicks(15));
    }

    [Fact]
    public void Two_falling_on_one_tick_rank_by_kills_then_by_seat()
    {
        // Slot 0 shoots slot 3 in the back while slot 3 shoots slot 1 in the back, 40 m each, the second pair the first moved 40 m on:
        // both shells land on the same tick. Slot 2 waits far off.
        var m = new Match(seed: 9);
        int s0 = m.Join(0), s1 = m.Join(1), s2 = m.Join(2), s3 = m.Join(3);
        m.Place(s0, 100, 190, 0f);
        m.Place(s3, 100, 150, 0f);
        m.Place(s1, 100, 110, 0f);
        m.Place(s2, 20, 20, 0f);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(s3, 1);
        m.Weaken(s1, 1);
        m.Attack(s0, s3);
        m.Attack(s3, s1);
        m.Run(2 * Tps);
        Assert.True(m.Tank(s3).IsDestroyed && m.Tank(s1).IsDestroyed);
        Assert.Equal(m.Rules.Of(1).EliminatedAt, m.Rules.Of(3).EliminatedAt);
        Assert.Equal(1, m.Rules.Of(3).Kills);
        m.Hold(s0);
        m.Place(s2, 140, 150, MathF.PI);   // 40 m in front of slot 0, clear of the wrecks
        m.Place(s0, 140, 190, 0f);
        m.Weaken(s2, 1);
        m.Attack(s0, s2);
        m.Run(4 * Tps);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchEndReason.Eliminated, m.Rules.EndReason);
        Assert.Equal(0, m.Rules.WinnerSlot);
        Assert.Equal((1, 2, 3, 4), (m.Rules.Of(0).Rank, m.Rules.Of(2).Rank, m.Rules.Of(3).Rank, m.Rules.Of(1).Rank));   // slot 3 has the kill

        // The same with no kills between them: by seat.
        var n = new Match(seed: 9);
        int t0 = n.Join(0), t1 = n.Join(1), t2 = n.Join(2), t3 = n.Join(3);
        n.Place(t0, 60, 150, 0f);
        n.Place(t2, 60, 110, 0f);
        n.Place(t1, 140, 150, 0f);
        n.Place(t3, 140, 110, 0f);
        n.Run(ModeRules.ProtectionTicks + 1);
        n.Weaken(t2, 1);
        n.Weaken(t3, 1);
        n.Attack(t0, t2);
        n.Attack(t1, t3);
        n.Run(2 * Tps);
        Assert.True(n.Tank(t2).IsDestroyed && n.Tank(t3).IsDestroyed);
        Assert.Equal(n.Rules.Of(2).EliminatedAt, n.Rules.Of(3).EliminatedAt);
        n.Hold(t1);
        n.Place(t0, 100, 190, 0f);
        n.Place(t1, 100, 150, 0f);
        n.Weaken(t1, 1);
        n.Attack(t0, t1);
        n.Run(4 * Tps);
        Assert.Equal(MatchPhase.Ended, n.Rules.Phase);
        Assert.Equal((1, 2, 3, 4), (n.Rules.Of(0).Rank, n.Rules.Of(1).Rank, n.Rules.Of(2).Rank, n.Rules.Of(3).Rank));
    }

    [Fact]
    public void Two_falling_on_one_tick_rank_by_the_health_they_had_before_it()
    {
        // As the second case above (two shells landing on one tick, no kills between the fallen), but slot 3 has more health left
        // going into that tick than slot 2: it places ahead though its seat comes later.
        var n = new Match(seed: 9);
        int t0 = n.Join(0), t1 = n.Join(1), t2 = n.Join(2), t3 = n.Join(3);
        n.Place(t0, 60, 150, 0f);
        n.Place(t2, 60, 110, 0f);
        n.Place(t1, 140, 150, 0f);
        n.Place(t3, 140, 110, 0f);
        n.Run(ModeRules.ProtectionTicks + 1);
        n.Weaken(t2, 1);
        n.Weaken(t3, 2);
        n.Attack(t0, t2);
        n.Attack(t1, t3);
        n.Run(2 * Tps);
        Assert.True(n.Tank(t2).IsDestroyed && n.Tank(t3).IsDestroyed);
        Assert.Equal(n.Rules.Of(2).EliminatedAt, n.Rules.Of(3).EliminatedAt);
        Assert.Equal((1, 1), (n.Rules.Of(0).Kills, n.Rules.Of(1).Kills));
        n.Hold(t1);
        n.Place(t0, 100, 190, 0f);
        n.Place(t1, 100, 150, 0f);
        n.Weaken(t1, 1);
        n.Attack(t0, t1);
        n.Run(4 * Tps);
        Assert.Equal(MatchPhase.Ended, n.Rules.Phase);
        Assert.Equal((1, 2, 3, 4), (n.Rules.Of(0).Rank, n.Rules.Of(1).Rank, n.Rules.Of(3).Rank, n.Rules.Of(2).Rank));
    }

    [Fact]
    public void A_royale_with_fewer_solo_points_than_players_shunts_the_rest_aside()
    {
        var few = new SpawnLayout(Array.Empty<SpawnPoint>(), Grid.Solo.Take(3).ToArray());   // 40 m apart in a row
        var m = new Match(spawns: few, seed: 4);
        Vector2[] at = m.Seat(7);
        for (int a = 0; a < at.Length; a++)
        {
            float nearest = few.Solo.Min(p => Vector2.Distance(p.Position, at[a]));
            Assert.True(nearest <= 18.01f, $"seat {a} at {at[a]}, {nearest:F1} m from the nearest point");
            for (int b = a + 1; b < at.Length; b++)
                Assert.True(Vector2.Distance(at[a], at[b]) >= 5.99f, $"seats {a} and {b} at {at[a]} and {at[b]}");
        }
        Assert.Equal(3, at.Take(3).Select(p => few.Solo.Single(s => s.Position == p)).Distinct().Count());   // the points themselves first
        Assert.Equal(at, new Match(spawns: few, seed: 4).Seat(7));
        Assert.Equal(7, m.Rules.Entrants);
    }

    [Fact]
    public void A_royale_on_the_clock_ranks_the_living_by_health()
    {
        var m = new Match(durationTicks: 60, seed: 2, ticksBefore: 4);
        int a = m.Join(0), b = m.Join(1), c = m.Join(2);
        m.Weaken(a, 300);
        m.Weaken(c, 500);
        m.Run(59);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        m.Run(1);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchEndReason.TimeUp, m.Rules.EndReason);
        Assert.Equal(1, m.Rules.WinnerSlot);   // b at full health
        Assert.Equal((1, 2, 3), (m.Rules.Of(1).Rank, m.Rules.Of(2).Rank, m.Rules.Of(0).Rank));
        Assert.All(new byte[] { 0, 1, 2 }, slot => Assert.Equal(60, m.Rules.LastedTicks(slot)));
        Assert.Equal(b, m.Rules.Of(1).TankId);
    }

    [Fact]
    public void Entrants_count_everyone_who_joined_and_not_those_who_left()
    {
        var m = new Match(BattleMode.FreeForAll);
        Assert.Equal(0, m.Rules.Entrants);
        m.Join(0);
        m.Join(4);
        m.Rules.Leave(0);
        Assert.Equal(2, m.Rules.Entrants);
        Assert.Equal(0, m.Rules.LastedTicks(0));   // left on the start tick
    }
}
