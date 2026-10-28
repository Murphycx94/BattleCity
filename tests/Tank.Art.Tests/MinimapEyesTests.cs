using Tank.Client.Match;
using Tank.Client.Minimap;
using Tank.Net.Battle;
using Tank.Sim.Geometry;
using SN = System.Numerics;

namespace Tank.Art.Tests;

public class MinimapEyesTests
{
    private const float CameraYaw = 1.1f, HalfFov = 0.8f, Tolerance = 1e-5f;

    private static readonly SN.Vector2 Own = new(120f, 340f);
    private static readonly SN.Vector2 Focus = new(500f, 60f);
    private const float OwnYaw = 0.4f;

    private static readonly RemoteTank Followed = new(new BattleEntity { Id = 7, Team = 0, Spec = 1 }, new SN.Vector2(250f, 260f),
        Yaw: -2f, TurretYaw: 0f, GunPitch: 0f, Speed: 0f, Extrapolated: false);

    private static MinimapEyeChoice Pick(bool hasTank, bool watching, SpectateState.Mode mode, RemoteTank? followed = null) =>
        MinimapEyes.Pick(hasTank, watching, mode, followed, Own, OwnYaw, Focus, CameraYaw, HalfFov);

    [Fact]
    public void Without_an_own_tank_there_is_no_eye_and_no_wreck()
    {
        Assert.Equal(default, Pick(hasTank: false, watching: false, SpectateState.Mode.Wreck));
        Assert.Equal(EyeKind.None, Pick(hasTank: false, watching: true, SpectateState.Mode.Follow, Followed).Eye.Kind);
    }

    [Fact]
    public void Fighting_or_under_a_fixed_preset_the_arrow_is_on_the_own_tank_along_its_hull()
    {
        // Not watching covers both: alive, and dead under a preset that keeps framing the own tank.
        MinimapEyeChoice choice = Pick(hasTank: true, watching: false, SpectateState.Mode.Follow, Followed);

        Assert.Equal(new MinimapEye(EyeKind.Arrow, Own, OwnYaw, CameraYaw, HalfFov), choice.Eye);
        Assert.Null(choice.OwnWreck);
        Assert.Equal(0, choice.EyeTankId);
    }

    [Fact]
    public void Following_a_tank_puts_the_arrow_on_it_leaves_it_out_of_the_others_and_crosses_the_own_wreck()
    {
        MinimapEyeChoice choice = Pick(hasTank: true, watching: true, SpectateState.Mode.Follow, Followed);

        Assert.Equal(new MinimapEye(EyeKind.Arrow, Followed.Position, Followed.Yaw, CameraYaw, HalfFov), choice.Eye);
        Assert.Equal(Own, choice.OwnWreck);
        Assert.Equal(7, choice.EyeTankId);
    }

    [Fact]
    public void The_free_camera_is_a_ring_on_its_focus_with_the_own_wreck_crossed()
    {
        MinimapEyeChoice choice = Pick(hasTank: true, watching: true, SpectateState.Mode.Free);

        Assert.Equal(EyeKind.Ring, choice.Eye.Kind);
        Assert.Equal(Focus, choice.Eye.Position);
        Assert.Equal(Own, choice.OwnWreck);
        Assert.Equal(0, choice.EyeTankId);
    }

    [Theory]
    [InlineData(SpectateState.Mode.Wreck)]    // the close-up
    [InlineData(SpectateState.Mode.Follow)]   // the frame a followed tank has left the snapshot
    public void The_close_up_is_a_ring_on_the_own_wreck(SpectateState.Mode mode)
    {
        MinimapEyeChoice choice = Pick(hasTank: true, watching: true, mode, followed: null);

        Assert.Equal(EyeKind.Ring, choice.Eye.Kind);
        Assert.Equal(Own, choice.Eye.Position);
        Assert.Equal(Own, choice.OwnWreck);
        Assert.Equal((CameraYaw, HalfFov), (choice.Eye.CameraYaw, choice.Eye.HalfFov));
    }

    [Fact]
    public void The_half_angle_widens_the_vertical_field_by_the_aspect_and_narrows_through_the_scope()
    {
        // 70° vertical on 16:9: tan(35°) · 16/9 = 1.2448 → 51.22°.
        Assert.Equal(51.22f * MathF.PI / 180f, MinimapEyes.HalfFov(70f, 16f / 9f), 3);
        Assert.Equal(35f * MathF.PI / 180f, MinimapEyes.HalfFov(70f, 1f), 5);
        Assert.True(MinimapEyes.HalfFov(70f / 4f, 16f / 9f) < MinimapEyes.HalfFov(70f, 16f / 9f) / 3f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(MathF.PI / 2)]
    [InlineData(-2.5f)]
    public void The_camera_yaw_is_the_heading_of_its_view_as_Angles_Forward_reads_it(float yaw)
    {
        // A camera pitched down by 0.3 rad at this yaw, built as OrbitCamera sets Rotation = (−pitch, yaw, 0).
        const float pitch = 0.3f;
        SN.Vector2 heading = Angles.Forward(yaw);
        var forward = new SN.Vector3(heading.X * MathF.Cos(pitch), -MathF.Sin(pitch), heading.Y * MathF.Cos(pitch));

        Assert.Equal(yaw, MinimapEyes.CameraYaw(forward, SN.Vector3.UnitY), Tolerance);
    }

    [Fact]
    public void Looking_straight_down_the_screen_s_up_is_the_heading()
    {
        // The "map" preset: straight down with −z at the top of the screen, which is yaw 0; +x at the top is yaw −π/2.
        Assert.Equal(0f, MinimapEyes.CameraYaw(-SN.Vector3.UnitY, -SN.Vector3.UnitZ), Tolerance);
        Assert.Equal(-MathF.PI / 2, MinimapEyes.CameraYaw(-SN.Vector3.UnitY, SN.Vector3.UnitX), Tolerance);
    }
}
