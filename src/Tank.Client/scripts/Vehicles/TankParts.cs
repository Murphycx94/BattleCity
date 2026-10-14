using Tank.Client.Art.Meshes;
using M = System.Numerics.Matrix4x4;
using SN = System.Numerics;

namespace Tank.Client.Vehicles;

/// <summary>Builds the tank's part meshes from its <see cref="TankLook"/>. Engine-neutral; TankModel turns them into nodes.</summary>
public static class TankParts
{
    /// <summary>Share of a wheel's radius from which outward it is the dark rubber tyre (vertex colour R = 0).</summary>
    public const float TyreStart = 0.74f;
    public const int DomeSides = 36;
    private const int WheelSides = 20;
    private const int BarrelSides = 18;
    private const int MantletSides = 18;
    private const int CapsuleSteps = 4;
    private const int DetailSides = 14;
    private const float SprocketTeethX = 0.15f;
    private const float BarrelIntoBulb = 0.3f;   // share of the bulb's length the barrel tube runs into, so no seam shows
    private static readonly M MirrorX = M.CreateScale(-1, 1, 1);
    private static readonly M AlongMinusZ = M.CreateRotationX(-MathF.PI / 2);   // lathe +Y → −Z (forward)
    private static readonly M AlongX = M.CreateRotationZ(-MathF.PI / 2);        // lathe +Y → +X
    private static readonly SN.Vector4 HubMask = new(0, 0, 0, 1);              // paint mode: 0 = hub colour, 1 = body colour

    /// <summary>Lower and upper hull plus every hull detail, merged into one mesh. Vertex colour R masks body (1) vs headlights (0).</summary>
    public static MeshData Hull(TankLook look)
    {
        var parts = new List<(MeshData, M)>
        {
            (RoundedSlab.Build(look.LowerHull, look.LowerHullWidth, look.LowerHullRound), M.Identity),
            (RoundedSlab.Build(look.UpperHull, look.UpperHullWidth, look.UpperHullRound), M.Identity),
        };
        parts.AddRange(Details(look, onTurret: false));
        return Merge.Build(parts);
    }

    /// <summary>Dome turret and its hatch in turret space (origin at the ring centre on the hull floor).</summary>
    public static MeshData Turret(TankLook look)
    {
        var parts = new List<(MeshData, M)> { (Lathe.Build(look.TurretProfile, DomeSides), M.CreateTranslation(0, look.TurretBase, 0)) };
        parts.AddRange(Details(look, onTurret: true));
        return Merge.Build(parts);
    }

    /// <summary>Capsule mantlet across the gun (axis X), centred on the visual pivot in gun space.</summary>
    public static MeshData Mantlet(TankLook look)
    {
        float r = look.MantletRadius, h = look.MantletWidth / 2 - r;
        var profile = new List<SN.Vector2>();
        for (int k = 0; k <= CapsuleSteps; k++)
        {
            float a = -MathF.PI / 2 + MathF.PI / 2 * k / CapsuleSteps;
            profile.Add(new SN.Vector2(k == 0 ? 0 : r * MathF.Cos(a), -h + r * MathF.Sin(a)));
        }
        for (int k = 0; k <= CapsuleSteps; k++)
        {
            float a = MathF.PI / 2 * k / CapsuleSteps;
            profile.Add(new SN.Vector2(k == CapsuleSteps ? 0 : r * MathF.Cos(a), h + r * MathF.Sin(a)));
        }
        return Lathe.Build(profile, MantletSides).Transformed(AlongX);
    }

    /// <summary>Plain fat barrel tube from the pivot along −Z, running into the muzzle bulb of a gun <paramref name="length"/> long.</summary>
    public static MeshData Barrel(TankLook look, float length)
    {
        float end = length - look.MuzzleLength * (1 - BarrelIntoBulb);
        var profile = new SN.Vector2[] { new(look.BarrelRadius, 0), new(look.BarrelRadius, end), new(0, end) };
        return Lathe.Build(profile, BarrelSides).Transformed(AlongMinusZ);
    }

    /// <summary>Round muzzle bulb with a shallow bore, ending exactly <paramref name="length"/> out along −Z.</summary>
    public static MeshData Muzzle(TankLook look, float length)
    {
        float r = look.MuzzleRadius, l = look.MuzzleLength, bore = look.BarrelRadius * 0.55f, depth = l * 0.3f;
        var profile = new SN.Vector2[]
        {
            new(0, 0), new(r * 0.72f, 0), new(r * 0.93f, l * 0.1f), new(r, l * 0.3f), new(r, l * 0.68f), new(r * 0.94f, l * 0.9f),
            new(r * 0.8f, l), new(bore, l), new(bore, l - depth), new(0, l - depth),
        };
        return Lathe.Build(profile, BarrelSides).Transformed(M.CreateTranslation(0, length - l, 0) * AlongMinusZ);
    }

    /// <summary>
    /// Toy wheel: a fat rounded tyre around a recessed disc with a hub cap, axle along X, hub facing outward (+X right, −X left).
    /// Vertex colour R is a paint mask: 1 on the hub disc, 0 on the rubber tyre and sprocket teeth.
    /// </summary>
    public static MeshData Wheel(float radius, float width, bool left)
    {
        float r = radius, w = width / 2;
        var profile = new SN.Vector2[]
        {
            new(0, -w), new(r * 0.7f, -w), new(r * 0.78f, -w), new(r * 0.94f, -w * 0.92f), new(r, -w * 0.55f), new(r, w * 0.55f),
            new(r * 0.94f, w * 0.92f), new(r * 0.78f, w), new(r * 0.7f, w * 0.86f), new(r * 0.36f, w * 0.86f), new(r * 0.3f, w + 0.05f),
            new(0, w + 0.05f),
        };
        MeshData wheel = Lathe.Build(profile, WheelSides).Transformed(AlongX);
        if (left) wheel = wheel.Transformed(MirrorX);
        return wheel.WithColors(p => new SN.Vector4(MathF.Sqrt(p.Y * p.Y + p.Z * p.Z) >= TyreStart * r ? 0f : 1f, 0, 0, 1));
    }

    /// <summary>Teeth the drive sprocket needs to mesh with the track: one per link around the pitch circle.</summary>
    public static int SprocketTeethCount(TankLook look) =>
        (int)MathF.Round(MathF.Tau * (look.Sprocket.Radius + look.TrackThickness / 2) / look.LinkPitch);

    /// <summary>
    /// Drive sprocket: a smaller toy wheel with two rings of teeth.
    /// Tooth k sits at angle 2πk/teeth from +Y towards +Z, so the wheel can be phased against the belt.
    /// </summary>
    public static MeshData Sprocket(float radius, float width, bool left, int teeth = 13)
    {
        var parts = new List<(MeshData, M)> { (Wheel(radius * 0.86f, width * 0.8f, left: false), M.Identity) };
        MeshData tooth = Box.Build(new SN.Vector3(0.08f, 0.14f, 0.11f), 0.02f).WithColor(new SN.Vector4(0, 0, 0, 1));
        foreach (float x in new[] { -SprocketTeethX, SprocketTeethX })
            for (int k = 0; k < teeth; k++)
                parts.Add((tooth, M.CreateTranslation(x, radius * 0.9f, 0) * M.CreateRotationX(MathF.Tau * k / teeth)));
        MeshData sprocket = Merge.Build(parts);
        return left ? sprocket.Transformed(MirrorX) : sprocket;
    }

    /// <summary>
    /// Circles the track's centre line wraps, in <see cref="BeltPath"/> order (counter-clockwise in the (z, y) side
    /// view): rear end wheel, front end wheel, then the road wheels front to rear. Radii include half the link thickness.
    /// </summary>
    public static Circle2[] TrackLoop(TankLook look)
    {
        float half = look.TrackThickness / 2;
        var ends = new[] { look.Sprocket, look.Idler }.OrderByDescending(w => w.Z);
        var loop = ends.Select(w => new Circle2(new SN.Vector2(w.Z, w.Y), w.Radius + half)).ToList();
        for (int i = 0; i < look.RoadWheelCount; i++)
            loop.Add(new Circle2(new SN.Vector2(look.RoadWheelZ(i), look.RoadWheelY), look.RoadWheelRadius + half));
        return loop.ToArray();
    }

    /// <summary>One chunky track shoe: a soft plate with one outer grouser. Local Y points out of the track loop.</summary>
    public static MeshData TrackLink(TankLook look)
    {
        float t = look.TrackThickness;
        return Merge.Build(
            (Box.Build(new SN.Vector3(look.TrackWidth, t, look.LinkPitch * 0.86f), 0.025f), M.Identity),
            (Box.Build(new SN.Vector3(look.TrackWidth * 0.9f, 0.03f, 0.07f), 0.01f), M.CreateTranslation(0, t / 2 + 0.014f, 0)));
    }

    private static IEnumerable<(MeshData, M)> Details(TankLook look, bool onTurret)
    {
        foreach (DetailPart part in look.Details.Where(d => d.OnTurret == onTurret))
        {
            M place = M.CreateTranslation(part.Position);
            SN.Vector3 s = part.Size;
            MeshData mesh = part.Kind switch
            {
                // Lamp along local −Z with a softly domed lens, painted in the hub colour.
                DetailKind.Headlight => Lathe.Build(new SN.Vector2[]
                    {
                        new(s.X / 2, -s.Z / 2), new(s.X / 2, s.Z / 2 - s.X * 0.15f), new(s.X * 0.36f, s.Z / 2), new(0, s.Z / 2 + s.X * 0.03f),
                    }, DetailSides).Transformed(AlongMinusZ).WithColor(HubMask),
                // Round hatch with a domed lid, standing on its base centre.
                _ => Lathe.Build(new SN.Vector2[]
                    {
                        new(s.X / 2, 0), new(s.X / 2, s.Y * 0.6f), new(s.X * 0.42f, s.Y * 0.9f), new(s.X * 0.25f, s.Y), new(0, s.Y),
                    }, DetailSides),
            };
            yield return (mesh, place);
        }
    }
}
