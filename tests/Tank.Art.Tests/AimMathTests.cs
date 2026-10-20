using System.Numerics;
using Tank.Client.Driving;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;

namespace Tank.Art.Tests;

public class AimMathTests
{
    private const int Samples = 101;
    private const float Cell = 2f;   // 200 m square, flat at y = 0

    private static Battlefield Flat() =>
        new(new HeightField(Samples, Samples, Cell, new float[Samples * Samples]), Array.Empty<Prop>(), 4f);

    private static Vector3 Trunnion(float x, float z) => new(x, 2f, z);

    [Fact]
    public void A_ray_that_hits_the_ground_aims_at_the_hit_point()
    {
        // Orbit focus 40 m east of the trunnion, 10 m up, looking 45 degrees down toward +X: lands 10 m further east.
        Vector3 look = Vector3.Normalize(new Vector3(1, -1, 0));
        Vector3 camera = new Vector3(50, 10, 100) - look * 14f;
        (float yaw, float pitch) = AimMath.Solve(Flat(), camera, look, 14f, sniper: false, Trunnion(10, 100), prevYaw: 0f);
        Vector3 expected = new Vector3(60, 0, 100) - Trunnion(10, 100);
        (float wantYaw, float wantPitch) = Angles.AimAngles(expected);
        Assert.Equal(wantYaw, yaw, 4);
        Assert.Equal(wantPitch, pitch, 4);
        Assert.True(pitch < 0, "aiming down at the ground");
    }

    [Fact]
    public void A_ray_that_misses_aims_at_the_end_of_the_range()
    {
        Vector3 look = Vector3.UnitX;   // level and over the map's edge: nothing to hit within the border
        Vector3 camera = new(20, 5, 100);
        var field = Flat();
        (float yaw, float pitch) = AimMath.Solve(field, camera, look, 14f, sniper: false, Trunnion(20, 100), prevYaw: 0f);
        Vector3 end = camera + look * (14f + AimMath.AimRange);
        (float wantYaw, float wantPitch) = Angles.AimAngles(end - Trunnion(20, 100));
        Assert.Equal(wantYaw, yaw, 4);
        Assert.Equal(wantPitch, pitch, 4);
    }

    [Fact]
    public void A_target_nearer_than_the_minimum_is_pushed_out_along_the_horizontal_look()
    {
        // Looking straight down 3 m east of the trunnion would give a yaw of a target almost underneath; it must go 8 m out toward +X.
        Vector3 look = Vector3.Normalize(new Vector3(0.05f, -1, 0));
        Vector3 trunnion = Trunnion(50, 100);
        Vector3 camera = new Vector3(53, 10, 100) - look * 14f;
        (float yaw, float pitch) = AimMath.Solve(Flat(), camera, look, 14f, sniper: false, trunnion, prevYaw: 1.2f);
        var target = new Vector3(trunnion.X + AimMath.MinAimDistance, 0f, trunnion.Z);
        (float wantYaw, float wantPitch) = Angles.AimAngles(target - trunnion);
        Assert.Equal(wantYaw, yaw, 3);
        Assert.Equal(wantPitch, pitch, 3);
    }

    [Fact]
    public void A_vertical_look_close_to_the_gun_keeps_the_previous_yaw()
    {
        Vector3 look = -Vector3.UnitY;
        Vector3 trunnion = Trunnion(50, 100);
        Vector3 camera = new Vector3(52, 10, 100) - look * 14f;
        (float yaw, _) = AimMath.Solve(Flat(), camera, look, 14f, sniper: false, trunnion, prevYaw: 1.2f);
        Assert.Equal(1.2f, yaw, 4);
    }

    [Fact]
    public void The_sniper_sight_casts_from_the_eye_not_the_orbit_focus()
    {
        Vector3 look = Vector3.Normalize(new Vector3(1, -1, 0));
        Vector3 camera = new(50, 10, 100);
        (float yaw, _) = AimMath.Solve(Flat(), camera, look, 14f, sniper: true, Trunnion(0, 100), prevYaw: 0f);
        // From the eye at x = 50 the ray lands at x = 60.
        Assert.Equal(Angles.AimAngles(new Vector3(60, 0, 100) - Trunnion(0, 100)).Yaw, yaw, 4);
    }
}
