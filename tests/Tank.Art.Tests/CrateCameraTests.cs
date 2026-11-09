using System.Numerics;
using Tank.Client.Art.Meshes;
using Tank.Client.Match;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Tank.Testing;

namespace Tank.Art.Tests;

/// <summary>
/// The "crate" camera preset (plan deviation 38): the known, standing crate nearest the tank, seen from 3.6 m out on the
/// sign's front side, turned 30° for a three-quarter view, 1.9 m over the ground, looking at 1.2 m over the crate.
/// </summary>
public class CrateCameraTests
{
    private static float Ground(float x, float z) => 10f + x * 0.01f + z * 0.02f;   // a gentle slope: the heights tell the points apart

    private static readonly Crate Near = new(5, new Vector2(120, 100), CrateKind.Reload);
    private static readonly Crate Far = new(2, new Vector2(300, 100), CrateKind.Armor);

    private static CrateKnowledge Knowing(params Crate[] crates)
    {
        var knowledge = new CrateKnowledge();
        knowledge.Reset(crates);
        return knowledge;
    }

    /// <summary>The sign's front (+Z in crate space) on the ground for crate <paramref name="id"/>, turned as the world turns it.</summary>
    private static Vector2 SignNormal(int id)
    {
        Vector3 front = Vector3.Transform(Vector3.UnitZ, Matrix4x4.CreateRotationY(id * CrateMesh.YawPerId));
        return new Vector2(front.X, front.Z);
    }

    [Fact]
    public void The_preset_is_named_crate()
    {
        Assert.Equal("crate", CrateCamera.Preset);
    }

    [Fact]
    public void It_frames_the_known_standing_crate_nearest_the_tank()
    {
        CrateKnowledge crates = Knowing(Far, Near);

        Assert.True(CrateCamera.TryPose(crates, new Vector2(100, 100), Ground, out Vector3 eye, out Vector3 focus));

        Assert.Equal(Near.Position.X, focus.X, 0.001f);
        Assert.Equal(Near.Position.Y, focus.Z, 0.001f);
        Assert.Equal(Ground(Near.Position.X, Near.Position.Y) + CrateCamera.FocusHeight, focus.Y, 0.001f);
        Assert.Equal(1.2f, CrateCamera.FocusHeight);
        Assert.True(float.IsFinite(eye.X) && float.IsFinite(eye.Y) && float.IsFinite(eye.Z));
    }

    [Fact]
    public void Fallen_and_unknown_crates_are_passed_over()
    {
        CrateKnowledge crates = Knowing(Far, Near);
        crates.Take(Near.Id);                                              // eaten: known, gone
        Assert.False(crates.All[7].Known);                                 // id 7 stands nearer still, but nobody has seen it

        Assert.True(CrateCamera.TryPose(crates, new Vector2(100, 100), Ground, out _, out Vector3 focus));

        Assert.Equal(Far.Position.X, focus.X, 0.001f);
        Assert.Equal(Far.Position.Y, focus.Z, 0.001f);
    }

    [Fact]
    public void Two_crates_as_near_go_to_the_lower_id()
    {
        CrateKnowledge crates = Knowing(new Crate(9, new Vector2(110, 100), CrateKind.Ammo), new Crate(4, new Vector2(90, 100), CrateKind.Optics));

        Assert.True(CrateCamera.TryPose(crates, new Vector2(100, 100), Ground, out _, out Vector3 focus));

        Assert.Equal(90f, focus.X, 0.001f);
    }

    [Fact]
    public void Nothing_known_or_nothing_standing_gives_no_pose()
    {
        Assert.False(CrateCamera.TryPose(new CrateKnowledge(), new Vector2(100, 100), Ground, out _, out _));
        CrateKnowledge eaten = Knowing(Near);
        eaten.Take(Near.Id);
        Assert.False(CrateCamera.TryPose(eaten, new Vector2(100, 100), Ground, out _, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(17)]
    [InlineData(63)]
    public void The_eye_stands_3_6_m_out_on_the_sign_s_front_side_1_9_m_over_the_ground(int id)
    {
        var crate = new Crate((byte)id, new Vector2(500, 400), CrateKind.Repair);

        Assert.True(CrateCamera.TryPose(Knowing(crate), new Vector2(480, 380), Ground, out Vector3 eye, out _));

        var flat = new Vector2(eye.X, eye.Z);
        Vector2 offset = flat - crate.Position;
        Assert.Equal(3.6f, offset.Length(), 0.01f);
        Assert.Equal(Ground(eye.X, eye.Z) + 1.9f, eye.Y, 0.001f);
        float facing = Vector2.Dot(Vector2.Normalize(offset), SignNormal(id));
        Assert.True(facing > 0f, $"crate {id}: the eye is behind the sign (cos {facing})");
        Assert.Equal(MathF.Cos(CrateCamera.Turn), facing, 0.001f);   // three-quarters: turned off the sign's normal
        Assert.Equal(MathF.PI / 6, CrateCamera.Turn, 0.001f);
    }

    [Fact]
    public void The_sign_s_facing_is_the_crate_space_front_turned_by_the_id()
    {
        for (int id = 0; id < CrateRules.MaxCrates; id++)
        {
            Vector2 expected = SignNormal(id);
            Vector2 got = CrateMesh.SignFacing(id);
            Assert.Equal(expected.X, got.X, 0.0001f);
            Assert.Equal(expected.Y, got.Y, 0.0001f);
        }
    }

    [Fact]
    public void A_pose_allocates_nothing()
    {
        CrateKnowledge crates = Knowing(Far, Near);
        Func<float, float, float> ground = Ground;
        Allocation.AssertNone(() =>
        {
            CrateCamera.TryPose(crates, new Vector2(100, 100), ground, out _, out _);
            var window = Allocation.Start();
            CrateCamera.TryPose(crates, new Vector2(100, 100), ground, out _, out _);
            return window.Stop();
        });
    }
}
