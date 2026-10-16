using System.Numerics;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Client.Vehicles;
using Tank.Sim.Vehicles;

namespace Tank.Art.Tests;

public class TankLookTests
{
    public static TheoryData<string> SpecIds => new(TankCatalog.All.Select(s => s.Id));

    /// <summary>Tanks whose gun sits in a turning turret (the tank destroyer's is in its casemate).</summary>
    public static TheoryData<string> TurretIds => new(TankCatalog.All.Where(s => s.HasTurret).Select(s => s.Id));

    /// <summary>Tanks whose gun sits in the casemate (no turret).</summary>
    public static TheoryData<string> CasemateIds => new(TankCatalog.All.Where(s => !s.HasTurret).Select(s => s.Id));

    private static float BarrelLength(TankLook look, TankSpec spec) => spec.GunLength + look.GunPivotZ;

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Every_catalog_tank_has_a_look(string specId)
    {
        Assert.Equal(specId, TankLooks.For(specId).SpecId);
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Look_has_a_turret_exactly_when_the_tank_does(string specId)
    {
        Assert.Equal(TankCatalog.ById(specId).HasTurret, TankLooks.For(specId).HasTurret);
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Every_part_is_a_closed_outward_solid(string specId)
    {
        TankLook look = TankLooks.For(specId);
        TankSpec spec = TankCatalog.All.Single(s => s.Id == specId);
        var parts = new Dictionary<string, MeshData>
        {
            ["hull"] = TankParts.Hull(look),
            ["mantlet"] = TankParts.Mantlet(look),
            ["barrel"] = TankParts.Barrel(look, BarrelLength(look, spec)),
            ["muzzle"] = TankParts.Muzzle(look, BarrelLength(look, spec)),
            ["left wheel"] = TankParts.Wheel(look.RoadWheelRadius, look.RoadWheelWidth, left: true),
            ["right wheel"] = TankParts.Wheel(look.RoadWheelRadius, look.RoadWheelWidth, left: false),
            ["left sprocket"] = TankParts.Sprocket(look.Sprocket.Radius, look.RoadWheelWidth, left: true),
            ["track link"] = TankParts.TrackLink(look),
        };
        if (look.HasTurret) parts["turret"] = TankParts.Turret(look);
        foreach (var (name, mesh) in parts)
        {
            MeshAssert.Closed(mesh);
            MeshAssert.WindingMatchesNormals(mesh);
            Assert.True(MeshAssert.SignedVolume(mesh) > 0, $"{name} is inside out");
        }
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Look_fits_the_sim_footprint(string specId)
    {
        TankLook look = TankLooks.For(specId);
        TankSpec spec = TankCatalog.All.Single(s => s.Id == specId);
        const float Slack = 0.03f;   // a hub cap or grouser may poke a hair past the sim's box
        var (min, max) = MeshAssert.Bounds(TankParts.Hull(look));
        Assert.InRange(min.X, -spec.HullWidth / 2 - Slack, 0);
        Assert.InRange(max.X, 0, spec.HullWidth / 2 + Slack);
        Assert.InRange(min.Z, -spec.HullLength / 2 - Slack, 0);
        Assert.InRange(max.Z, 0, spec.HullLength / 2 + Slack);
        float wheelOuter = look.TrackX + MeshAssert.Bounds(TankParts.Wheel(look.RoadWheelRadius, look.RoadWheelWidth, left: false)).Max.X;
        Assert.True(MathF.Max(wheelOuter, look.TrackX + look.TrackWidth / 2) <= spec.HullWidth / 2 + Slack, "running gear sticks out sideways");
        var belt = new BeltPath(TankParts.TrackLoop(look));
        for (int i = 0; i < 400; i++)
        {
            float z = belt.Sample(belt.Length * i / 400f).Position.X;
            Assert.InRange(MathF.Abs(z) + look.TrackThickness / 2, 0, spec.HullLength / 2 + Slack);
        }
        if (look.HasTurret) Assert.True(look.TurretProfile.Max(p => p.X) <= spec.HullWidth / 2, "turret wider than the hull");
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Paint_is_the_orange_toy_palette(string specId)
    {
        Assert.Equal(new TankPaint(Palette.TankBody, Palette.Track, Palette.TankHub), TankLooks.For(specId).Paint);   // shapes, not colours, tell them apart
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Hull_is_soft_and_round(string specId)
    {
        TankLook look = TankLooks.For(specId);
        foreach (var (profile, width, round) in new[]
                 { (look.LowerHull, look.LowerHullWidth, look.LowerHullRound), (look.UpperHull, look.UpperHullWidth, look.UpperHullRound) })
        {
            MeshData layer = RoundedSlab.Build(profile, width, round);
            Assert.True(MeshAssert.MaxCreaseDegrees(layer) <= 35f, $"crease of {MeshAssert.MaxCreaseDegrees(layer):F0}°");
            Assert.True(round >= 0.1f, "generous edge rounding");
        }
    }

    [Fact]
    public void Medium_turret_is_a_big_dome()
    {
        Vector2[] dome = TankLooks.Medium.TurretProfile;
        Assert.InRange(dome.Max(p => p.X), 1.35f, 1.55f);
        Assert.InRange(dome[^1].Y - dome[0].Y, 1.05f, 1.25f);
    }

    [Fact]
    public void Heavy_turret_is_a_rounded_block()
    {
        TankLook look = TankLooks.Heavy;
        Assert.True(look.TurretSquareness >= 3f, "squared off, not a dome");
        float side = look.TurretProfile.Max(p => p.X);
        Assert.True(look.TurretProfile.Count(p => MathF.Abs(p.X - side) < 1e-4f) >= 2, "straight sides");
        var (min, max) = MeshAssert.Bounds(TankParts.Turret(look));
        Assert.Equal(side, max.X, 3);   // the block's flat sides, not its corners, set its width
        Assert.Equal(-side, min.X, 3);
    }

    [Theory]
    [MemberData(nameof(TurretIds))]
    public void Turret_is_a_soft_closed_dome(string specId)
    {
        TankLook look = TankLooks.For(specId);
        Vector2[] dome = look.TurretProfile;
        Assert.Equal(0f, dome[^1].X);                                     // closed at the top
        for (int i = 1; i < dome.Length; i++) Assert.True(dome[i].Y > dome[i - 1].Y, "the dome rises all the way up");
        MeshData shell = Lathe.Build(dome, TankParts.DomeSides, squareness: look.TurretSquareness);   // as built, corners and all
        Assert.True(MeshAssert.MaxCreaseDegrees(shell, minY: 0.15f) <= 35f, $"dome crease {MeshAssert.MaxCreaseDegrees(shell, minY: 0.15f):F0}°");
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Barrel_and_muzzle_bulb_end_where_the_sim_puts_the_muzzle(string specId)
    {
        TankLook look = TankLooks.For(specId);
        float length = BarrelLength(look, TankCatalog.ById(specId));
        var (barrelMin, barrelMax) = MeshAssert.Bounds(TankParts.Barrel(look, length));
        var (muzzleMin, muzzleMax) = MeshAssert.Bounds(TankParts.Muzzle(look, length));
        Assert.Equal(-length, muzzleMin.Z, 4);
        Assert.True(barrelMax.Z <= 1e-4f, "nothing behind the pivot");
        Assert.True(barrelMin.Z < muzzleMax.Z - 0.02f, "the barrel runs into the bulb: no gap");
        Assert.True(barrelMin.Z > -length, "the bulb, not the tube, is the tip");
        Assert.Equal(look.BarrelRadius, barrelMax.X, 2);
        Assert.Equal(look.MuzzleRadius, muzzleMax.X, 2);
        Assert.True(look.MuzzleRadius > look.BarrelRadius * 1.2f, "a fat round bulb");
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Wheel_hubs_face_outward_on_both_sides(string specId)
    {
        TankLook look = TankLooks.For(specId);
        var (_, rightMax) = MeshAssert.Bounds(TankParts.Wheel(look.RoadWheelRadius, look.RoadWheelWidth, left: false));
        var (leftMin, _) = MeshAssert.Bounds(TankParts.Wheel(look.RoadWheelRadius, look.RoadWheelWidth, left: true));
        Assert.True(rightMax.X > look.RoadWheelWidth / 2, "right hub sticks out to +X");
        Assert.True(leftMin.X < -look.RoadWheelWidth / 2, "left hub sticks out to −X");
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Wheels_do_not_overlap_each_other(string specId)
    {
        TankLook look = TankLooks.For(specId);
        var wheels = Enumerable.Range(0, look.RoadWheelCount)
            .Select(i => new WheelSpot(look.RoadWheelZ(i), look.RoadWheelY, look.RoadWheelRadius))
            .Append(look.Sprocket).Append(look.Idler).ToArray();
        for (int i = 0; i < wheels.Length; i++)
        for (int j = i + 1; j < wheels.Length; j++)
        {
            float gap = Vector2.Distance(new(wheels[i].Z, wheels[i].Y), new(wheels[j].Z, wheels[j].Y)) - wheels[i].Radius - wheels[j].Radius;
            Assert.True(gap >= 0, $"wheels {i} and {j} overlap by {-gap:F3} m");
        }
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Track_sits_on_the_ground_and_runs_over_the_road_wheels(string specId)
    {
        TankLook look = TankLooks.For(specId);
        var belt = new BeltPath(TankParts.TrackLoop(look));
        float lowest = float.MaxValue;
        for (int i = 0; i < 500; i++) lowest = MathF.Min(lowest, belt.Sample(belt.Length * i / 500f).Position.Y);
        Assert.Equal(look.TrackThickness / 2, lowest, 3);
        float roadTop = look.RoadWheelY + look.RoadWheelRadius;
        foreach (WheelSpot end in new[] { look.Sprocket, look.Idler })
            Assert.True(end.Y + end.Radius >= roadTop, "the upper run must pass over the road wheels, not through them");
        Assert.InRange(belt.Length / look.LinkPitch, 40, 120);
    }

    /// <summary>
    /// The upper hull is one chunky body whose rounded side skirts swallow the upper track run: over the road wheels the
    /// body's underside stays below the run's inner face even at full nod — out to the tracks' outer edge, where the
    /// skirt's rounded bottom edge has already curled up a little — so no link ever shows through a gap. It stops near
    /// the road-wheel tops, so the wheels still show below it.
    /// </summary>
    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Side_skirts_hide_the_upper_track_run_but_not_the_road_wheels(string specId)
    {
        TankLook look = TankLooks.For(specId);
        var belt = new BeltPath(TankParts.TrackLoop(look));
        const float Cover = 0.01f;
        float gearOuter = MathF.Max(look.TrackX + look.TrackWidth / 2,
            look.TrackX + MeshAssert.Bounds(TankParts.Wheel(look.RoadWheelRadius, look.RoadWheelWidth, left: false)).Max.X);
        float r = look.UpperHullRound, flatEdge = look.UpperHullWidth / 2 - r;
        float edgeLift = gearOuter <= flatEdge ? 0 : r - MathF.Sqrt(r * r - (gearOuter - flatEdge) * (gearOuter - flatEdge));
        float outerRoadWheel = look.RoadWheelZ(look.RoadWheelCount - 1) + look.RoadWheelRadius;
        int checkedSamples = 0;
        for (int i = 0; i < 500; i++)
        {
            Vector2 p = belt.Sample(belt.Length * i / 500f).Position;   // (z, y) of the belt's centre line
            if (p.Y < look.RoadWheelY || MathF.Abs(p.X) > outerRoadWheel) continue;   // the upper run over the road wheels
            float bodyBottom = LowerEdge(look.UpperHull, p.X) + edgeLift + MathF.Abs(p.X) * MathF.Sin(TankAnimator.MaxNod);
            float runInner = p.Y - look.TrackThickness / 2;
            Assert.True(bodyBottom <= runInner - Cover, $"at z={p.X:F2} the run's inner face ({runInner:F3}) shows under the skirt ({bodyBottom:F3})");
            checkedSamples++;
        }
        Assert.True(checkedSamples > 20);
        Assert.True(look.UpperHullWidth / 2 >= gearOuter, "the skirts must reach outboard of tracks and hubs");
        float skirtBottom = look.UpperHull.Min(p => p.Y);
        Assert.InRange(skirtBottom, look.RoadWheelY + 0.2f, look.RoadWheelY + look.RoadWheelRadius);
        Assert.True(look.UpperHull.Max(p => p.Y) - skirtBottom >= 0.6f, "a fat body, not a thin plate");
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Lower_hull_fits_between_the_tracks(string specId)
    {
        TankLook look = TankLooks.For(specId);
        Assert.True(look.LowerHullWidth / 2 < look.TrackX - look.TrackWidth / 2, "lower hull rubs the tracks");
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Visible_muzzle_matches_the_sim_muzzle_across_the_pitch_range(string specId)
    {
        TankLook look = TankLooks.For(specId);
        TankSpec spec = TankCatalog.ById(specId);
        var trunnion = new Vector3(0, spec.TrunnionHeight, -spec.TurretOffset);
        foreach (float pitch in PitchRange(spec))
        {
            float deg = pitch * 180f / MathF.PI;
            var visual = trunnion + new Vector3(0, 0, look.GunPivotZ)
                + Vector3.Transform(new Vector3(0, 0, -BarrelLength(look, spec)), Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch));
            var sim = trunnion + Tank.Sim.Geometry.Angles.Direction(0, pitch) * spec.GunLength;
            Assert.True(Vector3.Distance(visual, sim) < 0.5f, $"{deg}°: visual muzzle is {Vector3.Distance(visual, sim):F2} m off");
        }
    }

    [Theory]
    [MemberData(nameof(TurretIds))]
    public void Mantlet_back_stays_buried_in_the_dome_across_the_pitch_range(string specId)
    {
        TankLook look = TankLooks.For(specId);
        TankSpec spec = TankCatalog.ById(specId);
        float r = look.MantletRadius, h = look.MantletWidth / 2 - r;   // capsule: cylinder half-length h, round ends
        Assert.True(h > 0, "a capsule, wider than tall");
        foreach (float pitch in PitchRange(spec))
        {
            float deg = pitch * 180f / MathF.PI;
            foreach (float x in new[] { -h, 0f, h })
            foreach (float back in new[] { r, 0.5f * r })
            {
                // Gun-space point (x, 0, back) behind the pivot, pitched about X, in turret space.
                float z = look.GunPivotZ + back * MathF.Cos(pitch);
                float y = spec.TrunnionHeight - back * MathF.Sin(pitch);
                float depth = DomeRadiusAt(look, y) - MathF.Sqrt(x * x + z * z);
                Assert.True(depth >= 0.05f, $"{deg}°: mantlet back at x={x:F2}, {back:F2} behind the pivot is only {depth:F3} m inside the dome");
            }
        }
        // ... and its front half shows: the pivot sits near the dome's surface, not deep inside.
        float surface = DomeRadiusAt(look, spec.TrunnionHeight);
        Assert.InRange(-look.GunPivotZ, surface - r * 0.5f, surface + r * 0.5f);
    }

    /// <summary>
    /// Depressing the gun fully while the hull rocks nose-up (a shot's nod) must not push the barrel, bulb or mantlet
    /// into the hull top. The hull nods about (0, RoadWheelY, 0); the turret is not tilted, it only follows the ring. A
    /// casemate's ball mount, and the stub of barrel inside the ball, sit in the front plate by design: only the barrel
    /// beyond the ball is checked, at both ends of the gun's arc as well as dead ahead (the gun yaws about the vertical
    /// through the ball's centre; the hull top is the side profile's, so only the barrel's z matters).
    /// </summary>
    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Gun_and_mantlet_clear_the_hull_top_at_full_depression_while_the_hull_nods(string specId)
    {
        TankLook look = TankLooks.For(specId);
        TankSpec spec = TankCatalog.ById(specId);
        const float Margin = 0.01f;
        float length = BarrelLength(look, spec);
        foreach (float nod in new[] { 0f, TankAnimator.MaxNod, -TankAnimator.MaxNod })
        {
            var pivot = new Vector3(0, look.RoadWheelY, 0);
            Quaternion bodyNod = Quaternion.CreateFromAxisAngle(Vector3.UnitX, nod), undo = Quaternion.Inverse(bodyNod);
            var ring = new Vector3(0, look.TurretBase, -spec.TurretOffset);
            Vector3 turretShift = pivot + Vector3.Transform(ring - pivot, bodyNod) - ring;   // TankModel.Animate
            var gunPivot = new Vector3(0, spec.TrunnionHeight, -spec.TurretOffset + look.GunPivotZ) + turretShift;
            foreach (float yaw in look.HasTurret ? new[] { 0f } : new[] { -spec.GunArc, 0f, spec.GunArc })
            foreach (float down in PitchRange(spec).Where(p => p <= 0))
            {
                float deg = down * 180f / MathF.PI, yawDeg = yaw * 180f / MathF.PI;
                Quaternion pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, down), turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
                var undersides = new List<(Vector3 Point, string Name)>();
                if (look.HasTurret) undersides.Add((gunPivot - new Vector3(0, look.MantletRadius, 0), "mantlet"));
                for (float d = look.HasTurret ? 0f : look.MantletRadius; d <= length; d += 0.05f)
                {
                    float radius = d >= length - look.MuzzleLength ? look.MuzzleRadius : look.BarrelRadius;
                    Vector3 underside = Vector3.Transform(Vector3.Transform(new Vector3(0, -radius, -d), pitch), turn);   // pitched in the gun, then yawed
                    undersides.Add((gunPivot + underside, d >= length - look.MuzzleLength ? "bulb" : "barrel"));
                }
                foreach (var (point, name) in undersides)
                {
                    Vector3 inBody = pivot + Vector3.Transform(point - pivot, undo);   // into the nodding hull's own frame
                    float top = UpperEdge(look.UpperHull, inBody.Z);
                    Assert.True(inBody.Y >= top + Margin,
                        $"{deg}° yawed {yawDeg:F0}° at nod {nod * 180 / MathF.PI:F0}°: {name} underside at z={inBody.Z:F2} is {top + Margin - inBody.Y:F3} m into the hull top");
                }
            }
        }
    }

    /// <summary>
    /// The tank destroyer's ball turns about its own centre (<see cref="TankLook.GunPivotZ"/> 0), so pitch and yaw leave
    /// it where it is: the sloped front plate runs through its middle, its front sticks out and its back is buried.
    /// </summary>
    [Fact]
    public void Td_ball_mount_sits_half_out_of_the_front_plate()
    {
        TankLook look = TankLooks.Td;
        TankSpec spec = TankCatalog.Td;
        Assert.Equal(0f, look.GunPivotZ);   // the ball turns about its own centre, so turning moves nothing but the barrel
        float z = -spec.TurretOffset, y = spec.TrunnionHeight, r = look.MantletRadius;
        Assert.True(y > UpperEdge(look.UpperHull, z - r), "the ball's front is buried in the plate");
        Assert.True(y < UpperEdge(look.UpperHull, z + r) - r * 0.5f, "the ball's back pokes out of the casemate");
        Assert.InRange(UpperEdge(look.UpperHull, z) - y, -r * 0.5f, r * 0.5f);   // the plate runs through the ball's middle
    }

    /// <summary>
    /// Without a turret, <see cref="TankLook.TurretBase"/> is where the gun mount rides the hull's nod: the trunnion's
    /// height, so the ball follows the front plate it sits in.
    /// </summary>
    [Theory]
    [MemberData(nameof(CasemateIds))]
    public void Casemate_gun_mount_rides_the_nod_at_the_trunnion(string specId)
    {
        Assert.Equal(TankCatalog.ById(specId).TrunnionHeight, TankLooks.For(specId).TurretBase, 1e-4f);
    }

    [Theory]
    [MemberData(nameof(TurretIds))]
    public void Turret_rests_on_the_hull_roof_and_the_mantlet_clears_it(string specId)
    {
        TankLook look = TankLooks.For(specId);
        TankSpec spec = TankCatalog.ById(specId);
        float roof = look.UpperHull.Max(p => p.Y);
        Assert.InRange(look.TurretBase, roof - 0.1f, roof);
        Assert.True(spec.TrunnionHeight - look.MantletRadius > roof, "the mantlet sinks into the hull roof");
        Assert.True(look.TurretBase + look.TurretProfile[^1].Y > spec.TrunnionHeight + look.MantletRadius, "the mantlet pokes over the dome");
    }

    [Fact]
    public void The_only_details_are_one_round_hatch_on_the_dome_and_two_headlights()
    {
        TankLook look = TankLooks.Medium;
        DetailPart hatch = Assert.Single(look.Details, d => d.OnTurret);
        Assert.Equal(DetailKind.Cupola, hatch.Kind);
        DetailPart[] lights = look.Details.Where(d => !d.OnTurret).ToArray();
        Assert.Equal(2, lights.Length);
        Assert.All(lights, l => Assert.Equal(DetailKind.Headlight, l.Kind));
        Assert.Equal(-lights[0].Position.X, lights[1].Position.X, 4);
        // The hatch rim is buried in the dome and its lid shows above it.
        float rim = new Vector2(hatch.Position.X, hatch.Position.Z).Length() + hatch.Size.X / 2;
        Assert.True(hatch.Position.Y < look.TurretBase + DomeHeightAt(look, rim), "hatch floats above the dome");
        Assert.True(hatch.Position.Y + hatch.Size.Y > look.TurretBase + look.TurretProfile[^1].Y, "hatch lid hidden in the dome");
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void Sprocket_has_one_tooth_per_track_link_around_the_pitch_circle(string specId)
    {
        TankLook look = TankLooks.For(specId);
        float pitchRadius = look.Sprocket.Radius + look.TrackThickness / 2;
        Assert.Equal((int)MathF.Round(MathF.Tau * pitchRadius / look.LinkPitch), TankParts.SprocketTeethCount(look));
    }

    [Fact]
    public void Wheels_carry_a_rubber_tyre_mask_in_vertex_colour()
    {
        TankLook look = TankLooks.Medium;
        MeshData wheel = TankParts.Wheel(look.RoadWheelRadius, look.RoadWheelWidth, left: false);
        Assert.True(wheel.HasColors);
        int rubber = 0, paint = 0;
        for (int i = 0; i < wheel.VertexCount; i++)
        {
            var p = wheel.Positions[i];
            bool outer = MathF.Sqrt(p.Y * p.Y + p.Z * p.Z) >= TankParts.TyreStart * look.RoadWheelRadius;
            Assert.Equal(outer ? 0f : 1f, wheel.Colors[i].X);
            if (outer) rubber++; else paint++;
        }
        Assert.True(rubber > 0 && paint > 0);
    }

    [Fact]
    public void Headlights_are_masked_for_the_hub_colour_and_the_hull_for_the_body_colour()
    {
        MeshData hull = TankParts.Hull(TankLooks.Medium);
        Assert.True(hull.HasColors);
        Assert.Contains(hull.Colors, c => c.X == 0f);
        Assert.Contains(hull.Colors, c => c.X == 1f);
    }

    /// <summary>The gun's whole pitch range in 1° steps, both limits included.</summary>
    private static IEnumerable<float> PitchRange(TankSpec spec)
    {
        const float Step = MathF.PI / 180f;
        for (float pitch = -spec.GunDepression; pitch < spec.GunElevation; pitch += Step) yield return pitch;
        yield return spec.GunElevation;
    }

    /// <summary>Lowest y of a convex (z, y) profile on the vertical line at <paramref name="z"/>; +∞ outside it.</summary>
    private static float LowerEdge(Vector2[] profile, float z)
    {
        float lowest = float.PositiveInfinity;
        for (int i = 0; i < profile.Length; i++)
        {
            Vector2 a = profile[i], b = profile[(i + 1) % profile.Length];
            if (MathF.Abs(b.X - a.X) < 1e-6f || z < MathF.Min(a.X, b.X) || z > MathF.Max(a.X, b.X)) continue;
            lowest = MathF.Min(lowest, a.Y + (b.Y - a.Y) * (z - a.X) / (b.X - a.X));
        }
        return lowest;
    }

    /// <summary>Highest y of a convex (z, y) profile on the vertical line at <paramref name="z"/>; −∞ outside it.</summary>
    private static float UpperEdge(Vector2[] profile, float z)
    {
        float highest = float.NegativeInfinity;
        for (int i = 0; i < profile.Length; i++)
        {
            Vector2 a = profile[i], b = profile[(i + 1) % profile.Length];
            if (MathF.Abs(b.X - a.X) < 1e-6f || z < MathF.Min(a.X, b.X) || z > MathF.Max(a.X, b.X)) continue;
            highest = MathF.Max(highest, a.Y + (b.Y - a.Y) * (z - a.X) / (b.X - a.X));
        }
        return highest;
    }

    /// <summary>Dome radius at turret-space height <paramref name="y"/>, from the lathe profile; 0 above the top.</summary>
    private static float DomeRadiusAt(TankLook look, float y)
    {
        Vector2[] dome = look.TurretProfile;
        float t = y - look.TurretBase;
        if (t <= dome[0].Y) return dome[0].X;
        for (int i = 1; i < dome.Length; i++)
            if (t <= dome[i].Y) return dome[i - 1].X + (dome[i].X - dome[i - 1].X) * (t - dome[i - 1].Y) / (dome[i].Y - dome[i - 1].Y);
        return 0;
    }

    /// <summary>Dome height above its base at distance <paramref name="radius"/> from the axis, searching from the top.</summary>
    private static float DomeHeightAt(TankLook look, float radius)
    {
        Vector2[] dome = look.TurretProfile;
        for (int i = dome.Length - 1; i > 0; i--)
            if (radius <= dome[i - 1].X)
                return dome[i].Y + (dome[i - 1].Y - dome[i].Y) * (radius - dome[i].X) / (dome[i - 1].X - dome[i].X);
        return dome[0].Y;
    }
}
