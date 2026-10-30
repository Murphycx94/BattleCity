using System.Numerics;
using Tank.Client.Match;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class ZoneCameraTests
{
    private static float Ground(float x, float z) => 10f + x * 0.01f;   // a gentle slope, so the heights tell the two points apart

    private static readonly ZoneRing Ring = new(new Vector2(600, 600), 420);

    [Fact]
    public void The_preset_is_named_zone()
    {
        Assert.Equal("zone", ZoneCamera.Preset);
    }

    [Fact]
    public void The_eye_stands_25_m_inside_the_wall_on_the_side_nearest_the_tank_and_looks_out_at_it()
    {
        (Vector3 eye, Vector3 target) = ZoneCamera.Pose(Ring, new Vector2(900, 600), Ground);

        Assert.Equal(995f, eye.X, 0.001f);
        Assert.Equal(600f, eye.Z, 0.001f);
        Assert.Equal(1020f, target.X, 0.001f);
        Assert.Equal(600f, target.Z, 0.001f);
        Assert.Equal(Ground(995, 600) + 5f, eye.Y, 0.001f);
        Assert.Equal(Ground(1020, 600) + 8f, target.Y, 0.001f);
        Assert.True(Ring.Contains(new Vector2(eye.X, eye.Z)), "the eye is inside the circle");
        Assert.Equal(Ring.Radius, Vector2.Distance(new Vector2(target.X, target.Z), Ring.Centre), 0.001f);   // the target is on the wall
    }

    [Fact]
    public void The_side_follows_the_tank_round_the_circle()
    {
        (Vector3 eye, Vector3 target) = ZoneCamera.Pose(Ring, new Vector2(600, 100), Ground);

        Assert.Equal(600f, eye.X, 0.001f);
        Assert.Equal(205f, eye.Z, 0.001f);   // 600 − 395
        Assert.Equal(180f, target.Z, 0.001f);
    }

    [Fact]
    public void A_tank_on_the_centre_gives_a_pose_without_nan()
    {
        (Vector3 eye, Vector3 target) = ZoneCamera.Pose(Ring, Ring.Centre, Ground);

        Assert.True(float.IsFinite(eye.X) && float.IsFinite(eye.Y) && float.IsFinite(eye.Z));
        Assert.True(float.IsFinite(target.X) && float.IsFinite(target.Y) && float.IsFinite(target.Z));
        Assert.Equal(995f, eye.X, 0.001f);     // the +X side when there is no nearest side
        Assert.Equal(1020f, target.X, 0.001f);
    }

    [Fact]
    public void A_circle_smaller_than_the_inset_keeps_the_eye_on_the_centre()
    {
        (Vector3 eye, Vector3 target) = ZoneCamera.Pose(new ZoneRing(new Vector2(500, 500), 10), new Vector2(900, 500), Ground);

        Assert.Equal(500f, eye.X, 0.001f);
        Assert.Equal(500f, eye.Z, 0.001f);
        Assert.Equal(510f, target.X, 0.001f);
    }

    [Fact]
    public void A_closed_circle_looks_down_on_its_centre_from_40_m()
    {
        var closed = new ZoneRing(new Vector2(500, 500), 0.5f);

        (Vector3 eye, Vector3 target) = ZoneCamera.Pose(closed, new Vector2(900, 500), Ground);

        Assert.Equal(new Vector3(500, Ground(500, 500) + 40f, 500), eye);
        Assert.Equal(new Vector3(500, Ground(500, 500), 500), target);
    }
}
