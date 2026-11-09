using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Net.Tests.Battle;

public sealed class BattleServerUnitTests
{
    private static InputCmd Cmd(int tick, sbyte throttle = 1, bool fire = false) => new(tick, throttle, 0, 1.5f, -0.1f, fire, AmmoType.HE);

    [Fact]
    public void Input_buffer_dedupes_drops_late_and_repeats_without_firing_then_idles()
    {
        var buffer = new BattleInputBuffer();
        buffer.Add(Cmd(10, fire: true), currentTick: 8);
        buffer.Add(Cmd(10, fire: true), currentTick: 8);   // redundant copy
        buffer.Add(Cmd(9), currentTick: 9);                 // late: tick 9 is already simulated
        buffer.Add(Cmd(200), currentTick: 9);               // far ahead: bogus
        Assert.Equal(1, buffer.LateCount);
        Assert.Equal(10, buffer.NewestReceivedTick);
        Assert.Equal(1, buffer.SlackAt(9));

        Assert.Equal(Cmd(10, fire: true), buffer.Take(10));
        buffer.Add(Cmd(10, fire: true), currentTick: 10);   // a redundant copy of a consumed tick isn't late
        Assert.Equal(1, buffer.LateCount);
        for (int tick = 11; tick <= 10 + BattleInputBuffer.MaxRepeatedTicks; tick++)
        {
            InputCmd repeated = buffer.Take(tick);
            Assert.Equal(1, repeated.Throttle);
            Assert.False(repeated.Fire);
            Assert.Equal(1.5f, repeated.AimYaw);
            Assert.Equal(AmmoType.HE, repeated.Ammo);
        }
        InputCmd idle = buffer.Take(17);
        Assert.Equal(0, idle.Throttle);
        Assert.Equal(1.5f, idle.AimYaw);   // the gun stays where it was
        Assert.Equal(AmmoType.HE, idle.Ammo);
        Assert.Equal(7, buffer.MissingCount);
        buffer.Add(Cmd(5), currentTick: 17);   // older than what was consumed
        Assert.Equal(2, buffer.LateCount);

        buffer.Reset();   // a new match: last match's ticks mean nothing, and a copy of one is now simply late
        Assert.Equal((0, 0, -1), (buffer.LateCount, buffer.MissingCount, buffer.NewestReceivedTick));
        buffer.Add(Cmd(10), currentTick: 10);
        Assert.Equal(1, buffer.LateCount);
    }

    private static (Simulation Sim, VisionTracker Vision, int Me, int Mate, int Near, int Far) World()
    {
        var sim = new Simulation(TestWorld.FlatField());
        int me = sim.AddTank(TankCatalog.Medium, new Vector2(100, 190), 0f, 0);
        int mate = sim.AddTank(TankCatalog.Medium, new Vector2(60, 190), 0f, 0);
        int near = sim.AddTank(TankCatalog.Light, new Vector2(100, 140), MathF.PI / 2, 1);   // 50 m: seen
        int far = sim.AddTank(TankCatalog.Light, new Vector2(100, 10), MathF.PI / 2, 1);     // 180 m: beyond a still light tank's 168 m
        var vision = new VisionTracker(sim);
        Span<InputCmd> inputs = stackalloc InputCmd[4];
        for (int i = 0; i < 4; i++) inputs[i] = new InputCmd(1, 0, 0, sim.Tanks[i].Yaw, 0f, false);
        sim.Step(inputs);
        vision.Update();
        return (sim, vision, me, mate, near, far);
    }

    [Fact]
    public void A_viewer_sees_its_team_wrecks_and_what_its_team_spots()
    {
        var (sim, vision, me, mate, near, far) = World();
        var viewer = new Viewer(0, me, false, SeesAllWhenDead: false);
        Assert.True(viewer.SeesTank(me, sim, vision));
        Assert.True(viewer.SeesTank(mate, sim, vision));
        Assert.True(viewer.SeesTank(near, sim, vision));
        Assert.False(viewer.SeesTank(far, sim, vision));
        Assert.True(viewer.Sees(sim.Tanks[sim.SlotOf(far)] with { IsDestroyed = true }, vision), "wrecks are seen by all");
        Assert.False(viewer.SeesTank(999, sim, vision));
    }

    [Fact]
    public void A_dead_viewer_in_a_free_for_all_sees_everything_a_dead_team_viewer_does_not()
    {
        var (sim, vision, me, _, _, far) = World();
        Assert.False(new Viewer(0, me, OwnDestroyed: true, SeesAllWhenDead: false).SeesTank(far, sim, vision));
        Assert.True(new Viewer(0, me, OwnDestroyed: true, SeesAllWhenDead: true).SeesTank(far, sim, vision));
        Assert.True(new Viewer(0, 0, false, SeesAllWhenDead: true).SeesTank(far, sim, vision), "no tank yet: watching");
        Assert.False(new Viewer(0, me, false, SeesAllWhenDead: true).SeesTank(far, sim, vision), "alive: own eyes only");
    }

    [Fact]
    public void Events_are_filtered_by_what_the_viewer_sees()
    {
        var (sim, vision, me, _, near, far) = World();
        var viewer = new Viewer(0, me, false, false);
        var shotByFar = new GameEvent(GameEventType.ShellFired, 1, far, 50, Vector3.One, Vector3.UnitZ);
        Assert.True(viewer.Wants(shotByFar, sim, vision, out GameEvent sent));
        Assert.Equal(0, sent.TankId);   // a shot from the dark: no shooter
        var shotByNear = shotByFar with { TankId = near };
        Assert.True(viewer.Wants(shotByNear, sim, vision, out sent));
        Assert.Equal(near, sent.TankId);

        var hitOnFar = new GameEvent(GameEventType.TankHit, 1, near, 51, Vector3.One, Vector3.UnitZ, TargetTankId: far, Damage: 100);
        Assert.False(viewer.Wants(hitOnFar, sim, vision, out _));
        Assert.True(viewer.Wants(hitOnFar with { TankId = me }, sim, vision, out _), "my own hit on it");
        Assert.True(viewer.Wants(hitOnFar with { TargetTankId = near }, sim, vision, out _));
        Assert.True(viewer.Wants(new GameEvent(GameEventType.ShellExploded, 1, far, 50, Vector3.One, Vector3.Zero), sim, vision, out _));
        Assert.True(viewer.Wants(new GameEvent(GameEventType.PropDestroyed, 1, far, 50, Vector3.One, Vector3.Zero, PropId: 3), sim, vision, out _));
        Assert.False(viewer.Wants(new GameEvent(GameEventType.TankRevived, 1, far, 0, Vector3.One, Vector3.Zero, TargetTankId: far), sim, vision, out _));
        Assert.True(viewer.Wants(new GameEvent(GameEventType.TankRevived, 1, near, 0, Vector3.One, Vector3.Zero, TargetTankId: near), sim, vision, out _));
    }

    [Theory]
    [InlineData(GameEventType.TankHit)]
    [InlineData(GameEventType.SplashDamage)]
    [InlineData(GameEventType.TankDestroyed)]
    public void A_viewer_without_a_tank_is_not_the_shooter_of_an_unattributed_hit(GameEventType type)
    {
        // A shooter the server blanked or never had is tank 0; a spectator (own tank 0) must not mistake that for "mine".
        var (sim, vision, me, _, near, far) = World();
        var unattributedOnFar = new GameEvent(type, 1, 0, 51, Vector3.One, Vector3.UnitZ, TargetTankId: far, Damage: 100);
        var spectator = new Viewer(0, 0, false, false);
        Assert.False(spectator.Wants(unattributedOnFar, sim, vision, out _), "the spectator doesn't see the target, and fired nothing");
        Assert.True(spectator.Wants(unattributedOnFar with { TargetTankId = near }, sim, vision, out _), "but it still hears of a target its team sees");

        var player = new Viewer(0, me, false, false);
        Assert.False(player.Wants(unattributedOnFar, sim, vision, out _), "a player with a tank isn't tank 0 either");
        Assert.True(player.Wants(unattributedOnFar with { TankId = me }, sim, vision, out _), "its own shot is always its own");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(BattleServerOptions.MaxMatchSeconds + 1)]
    public void A_match_length_outside_the_accepted_range_is_refused(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleServerOptions { MatchSeconds = seconds });
        Assert.Equal(5.0, new BattleServerOptions { MatchSeconds = 5 }.MatchSeconds);
        Assert.Null(new BattleServerOptions().MatchSeconds);
    }

    [Fact]
    public void Spotted_means_an_enemy_team_sees_me()
    {
        var (sim, vision, me, _, near, far) = World();
        Assert.True(new Viewer(0, me, false, false).IsSpotted(sim, vision), "the light tank 50 m away sees me");
        Assert.False(new Viewer(1, far, false, false).IsSpotted(sim, vision), "nobody sees the far one");
        Assert.False(new Viewer(0, me, OwnDestroyed: true, false).IsSpotted(sim, vision));
        _ = near;
    }

    [Fact]
    public void Tanks_quantise_to_the_wire_and_the_own_tank_is_exact()
    {
        var t = new TankState
        {
            Id = 9, Spec = TankCatalog.Heavy, Team = 1, Position = new Vector2(123.456f, 78.9f), Yaw = 1.1f, TurretYaw = -0.4f, GunPitch = 0.12f,
            Speed = -2.34f, Reload = 1.5f, Bloom = 0.3f, Ammo = AmmoType.HE, ApLeft = 10, HeLeft = 4, ShotCount = 6, Health = 1200, ProtectedUntil = 160, LastFired = 12,
        };
        BattleEntity e = BattleServer.ToEntity(t, 4, 2, tick: 100);
        Assert.Equal((12346, 7890), (e.X, e.Z));
        Assert.Equal(1.1f, BattleProtocol.DequantizeAngle(e.Yaw, BattleProtocol.YawBits), 0.002f);
        Assert.Equal(-0.4f, BattleProtocol.DequantizeAngle(e.TurretYaw, BattleProtocol.YawBits), 0.002f);
        Assert.Equal(0.12f, BattleProtocol.DequantizePitch(e.Pitch, BattleProtocol.PitchBits), 0.004f);
        Assert.Equal(-23, e.Speed);
        Assert.Equal(1200, e.Health);
        Assert.Equal(TankFlags.Protected, e.Flags);
        Assert.Equal(TankFlags.None, BattleServer.ToEntity(t, 4, 2, tick: 160).Flags);   // the window has closed
        Assert.Equal(TankFlags.Destroyed, BattleServer.ToEntity(t with { IsDestroyed = true }, 4, 2, tick: 200).Flags);
        Assert.Equal((4, 2, 1), (e.Slot, e.Spec, e.Team));
        OwnTank own = BattleServer.ToOwn(t);
        Assert.Equal((123.456f, 78.9f, 1.1f, -0.4f, 0.12f, -2.34f, 1.5f, 0.3f), (own.X, own.Z, own.Yaw, own.TurretYaw, own.GunPitch, own.Speed, own.Reload, own.Bloom));
        Assert.Equal((AmmoType.HE, 10, 4, 6, 1200, 160, 12, false), (own.Ammo, own.ApLeft, own.HeLeft, own.ShotCount, own.Health, own.ProtectedUntil, own.LastFired, own.Destroyed));
    }
}
