using System.Numerics;
using Tank.Sim.Modes;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// A supply crate as drawn (stage 6 design §4.3): a wooden box the size of the simulation's crate
/// (<see cref="CrateRules.HalfSize"/>, <see cref="CrateRules.Height"/>) standing on the ground at the origin, and over it a
/// post carrying a sign in the kind's colour (<see cref="Palette.Crate"/>) with a white icon in relief on both faces: a
/// cross (repair), a shell standing on end (ammo), a shield (armour), an open ring with an arrowhead (reload), two rings
/// side by side (optics). Vertex colours are linear; the house shader draws them. Crate space: y up, the sign's front
/// faces +Z; <c>CrateViews</c> turns each crate by its id (<see cref="YawPerId"/>, so the sign faces <see cref="SignFacing"/>). <see cref="Build"/> gives the box and the sign (with its post)
/// as two meshes: the sign's origin is the plate's centre, so a view bobs it by moving that node between
/// <see cref="SignCentreHeight"/> ± <see cref="BobAmplitude"/> every <see cref="BobPeriod"/> seconds. The plate's top edge
/// at rest is <see cref="CrateRules.SignTop"/>, what the simulation tests sight against.
/// </summary>
public static class CrateMesh
{
    /// <summary>m above the ground: the plate's centre at rest.</summary>
    public const float SignCentreHeight = 1.55f;

    /// <summary>m: the sign bobs this far up and down from <see cref="SignCentreHeight"/>.</summary>
    public const float BobAmplitude = 0.12f;

    /// <summary>s: one bob up and down.</summary>
    public const float BobPeriod = 2.4f;

    /// <summary>rad: crate <c>id</c> stands turned about y by <c>id × YawPerId</c>, so a row of crates doesn't line up.</summary>
    public const float YawPerId = 0.7f;

    public const float BodySize = 2f * CrateRules.HalfSize;
    public const float BodyHeight = CrateRules.Height;
    public const float SignWidth = 0.7f, SignHeight = 0.5f, SignThickness = 0.06f;
    public const float PostWidth = 0.08f, PostHeight = 0.9f;

    private const float BodyBevel = 0.05f, SignBevel = 0.01f;
    private const float PostIntoSign = 0.05f;   // the post's top runs this far up into the plate, so no gap shows while it bobs
    private const float StrapLength = 1.0f, StrapHeight = 0.07f, StrapThickness = 0.03f, StrapProud = 0.01f;
    private const float IconThickness = 0.02f, IconProud = 0.015f;   // the icon stands this far out of the plate's face
    private const int IconSegments = 16;
    private static readonly float[] StrapHeights = { 0.28f, 0.72f };

    /// <summary>The box: beveled wood with two plank straps a little proud of each of the four sides, its bottom at y = 0.</summary>
    public static MeshData Body()
    {
        Vector4 wood = Colour(Palette.Wood), planks = Colour(Palette.Planks);
        var parts = new List<(MeshData, Matrix4x4)>
        {
            (Box.Build(new Vector3(BodySize, BodyHeight, BodySize), BodyBevel).WithColor(wood), At(0f, BodyHeight / 2, 0f)),
        };
        MeshData alongZ = Box.Build(new Vector3(StrapThickness, StrapHeight, StrapLength)).WithColor(planks);   // on the ±X faces
        MeshData alongX = Box.Build(new Vector3(StrapLength, StrapHeight, StrapThickness)).WithColor(planks);   // on the ±Z faces
        float reach = CrateRules.HalfSize + StrapProud - StrapThickness / 2;   // the strap's centre: its outer face StrapProud out of the box's
        foreach (float y in StrapHeights)
        {
            parts.Add((alongZ, At(reach, y, 0f)));
            parts.Add((alongZ, At(-reach, y, 0f)));
            parts.Add((alongX, At(0f, y, reach)));
            parts.Add((alongX, At(0f, y, -reach)));
        }
        return Merge.Build(parts);
    }

    /// <summary>
    /// Which way crate <paramref name="id"/>'s sign faces on the ground, (x, z), unit length: crate space's +Z turned by
    /// <c>id × </c><see cref="YawPerId"/> about y (from +Z towards +X), as the world turns the crate.
    /// </summary>
    public static Vector2 SignFacing(int id)
    {
        float yaw = id * YawPerId;
        return new Vector2(MathF.Sin(yaw), MathF.Cos(yaw));
    }

    /// <summary>The dark post, in crate space: its foot inside the box, its top <see cref="PostIntoSign"/> up into the plate at rest.</summary>
    public static MeshData Post()
    {
        float top = SignCentreHeight - SignHeight / 2 + PostIntoSign;
        return Box.Build(new Vector3(PostWidth, PostHeight, PostWidth)).WithColor(Colour(Palette.Door)).Transformed(At(0f, top - PostHeight / 2, 0f));
    }

    /// <summary>
    /// The plate in <paramref name="kind"/>'s colour, centred at <see cref="SignCentreHeight"/> in crate space, with the kind's
    /// white icon in relief on the front (+Z) and, turned round, on the back. ArgumentOutOfRangeException for a kind the set doesn't have.
    /// </summary>
    public static MeshData Sign(CrateKind kind)
    {
        MeshData plate = Box.Build(new Vector3(SignWidth, SignHeight, SignThickness), SignBevel).WithColor(Colour(Palette.Crate(kind)));
        MeshData icon = Icon(kind).WithColor(Colour(Palette.UiText));
        float face = SignThickness / 2 + IconProud - IconThickness / 2;   // the icon's centre: IconProud of it outside the plate
        return Merge.Build(
            (plate, At(0f, SignCentreHeight, 0f)),
            (icon, At(0f, SignCentreHeight, face)),
            (icon, Matrix4x4.CreateRotationY(MathF.PI) * At(0f, SignCentreHeight, -face)));
    }

    /// <summary>
    /// The whole crate as two meshes: the box (<see cref="Body"/>, on the ground at the origin) and the sign with its post,
    /// moved down so the plate's centre is the origin: a view puts that node at <see cref="SignCentreHeight"/> and bobs it.
    /// </summary>
    public static (MeshData Box, MeshData Sign) Build(CrateKind kind)
    {
        Matrix4x4 drop = At(0f, -SignCentreHeight, 0f);
        return (Body(), Merge.Build((Post(), drop), (Sign(kind), drop)));
    }

    /// <summary>The kind's icon: in the XY plane, centred on the origin, <see cref="IconThickness"/> thick about z = 0, uncoloured.</summary>
    private static MeshData Icon(CrateKind kind) => kind switch
    {
        CrateKind.Repair => Cross(),
        CrateKind.Ammo => Shell(),
        CrateKind.Armor => Shield(),
        CrateKind.Reload => OpenRingWithArrow(),
        CrateKind.Optics => TwoRings(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a crate kind."),
    };

    /// <summary>Repair: a cross of two bars.</summary>
    private static MeshData Cross() => Merge.Build(
        (Box.Build(new Vector3(0.26f, 0.08f, IconThickness)), Matrix4x4.Identity),
        (Box.Build(new Vector3(0.08f, 0.26f, IconThickness)), Matrix4x4.Identity));

    /// <summary>Ammo: a shell standing on end (a lathe: a cylinder and a point), flattened to the icon's thickness.</summary>
    private static MeshData Shell()
    {
        const float radius = 0.05f;
        var profile = new[] { new Vector2(radius, -0.14f), new Vector2(radius, 0.03f), new Vector2(0f, 0.15f) };
        return Lathe.Build(profile, 12).Transformed(Matrix4x4.CreateScale(1f, 1f, IconThickness / (2f * radius)));
    }

    /// <summary>Armour: a shield, a pentagon coming to a point at the bottom.</summary>
    private static MeshData Shield()
    {
        var outline = new[] { new Vector2(-0.12f, 0.11f), new Vector2(0.12f, 0.11f), new Vector2(0.12f, -0.03f), new Vector2(0f, -0.15f), new Vector2(-0.12f, -0.03f) };
        return Prism.Build(outline, IconThickness);
    }

    /// <summary>Reload: three quarters of a ring, open towards +X, with an arrowhead on its end pointing on round.</summary>
    private static MeshData OpenRingWithArrow()
    {
        const float inner = 0.075f, outer = 0.115f, fromTurn = 0.125f, toTurn = 0.875f;
        const float headLength = 0.07f, headHalfWidth = 0.05f;
        float end = toTurn * MathF.Tau, middle = (inner + outer) / 2;
        var head = new[] { new Vector2(headLength / 2, 0f), new Vector2(-headLength / 2, headHalfWidth), new Vector2(-headLength / 2, -headHalfWidth) };   // points along +X
        Matrix4x4 onTheEnd = Matrix4x4.CreateRotationZ(end + MathF.PI / 2)   // the tangent going on round the ring
            * At(middle * MathF.Cos(end), middle * MathF.Sin(end), 0f);
        return Merge.Build(
            (Arc(inner, outer, IconThickness, fromTurn, toTurn, IconSegments), Matrix4x4.Identity),
            (Prism.Build(head, IconThickness), onTheEnd));
    }

    /// <summary>Optics: two rings side by side, a pair of binoculars seen from the front.</summary>
    private static MeshData TwoRings()
    {
        const float inner = 0.045f, outer = 0.075f, apart = 0.085f;
        MeshData ring = Arc(inner, outer, IconThickness, 0f, 1f, IconSegments);
        return Merge.Build((ring, At(-apart, 0f, 0f)), (ring, At(apart, 0f, 0f)));
    }

    /// <summary>
    /// A flat ring in the XY plane about the origin, or a piece of one: the band between <paramref name="inner"/> and
    /// <paramref name="outer"/>, <paramref name="thickness"/> thick about z = 0, from <paramref name="fromTurn"/> to
    /// <paramref name="toTurn"/> of a turn (0…1, from +X towards +Y) in <paramref name="segments"/> pieces. A whole turn
    /// closes on itself; a piece of one is capped at both ends. A closed solid: front and back faces, the outer and inner
    /// walls (normals along the radius) and the caps (normals along the tangent) have their own vertices, so every face shades flat.
    /// </summary>
    public static MeshData Arc(float inner, float outer, float thickness, float fromTurn, float toTurn, int segments)
    {
        if (!(inner >= 0f)) throw new ArgumentOutOfRangeException(nameof(inner), inner, "The inner radius is 0 or more.");
        if (!(outer > inner) || !float.IsFinite(outer)) throw new ArgumentOutOfRangeException(nameof(outer), outer, $"The outer radius is finite and above the inner {inner}.");
        if (!(thickness > 0f) || !float.IsFinite(thickness)) throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "The thickness is positive.");
        if (!(fromTurn >= 0f) || !(toTurn <= 1f) || !(toTurn > fromTurn)) throw new ArgumentOutOfRangeException(nameof(toTurn), toTurn, $"The turn runs from {fromTurn} up to 1.");
        if (segments < 3) throw new ArgumentOutOfRangeException(nameof(segments), segments, "A ring needs at least 3 segments.");
        bool whole = toTurn - fromTurn >= 1f - 1e-6f;
        int columns = whole ? segments : segments + 1;
        float half = thickness / 2;
        var b = new MeshBuilder();
        for (int k = 0; k < columns; k++)
        {
            float turn = fromTurn + (toTurn - fromTurn) * k / segments;
            float angle = turn * MathF.Tau;
            var radial = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f);
            Vector3 inAt = radial * inner, outAt = radial * outer, front = Vector3.UnitZ * half;
            var uv = new Vector2(turn, 0f);
            b.AddVertex(inAt + front, Vector3.UnitZ, uv);     // 0 front, inner
            b.AddVertex(outAt + front, Vector3.UnitZ, uv);    // 1 front, outer
            b.AddVertex(inAt - front, -Vector3.UnitZ, uv);    // 2 back, inner
            b.AddVertex(outAt - front, -Vector3.UnitZ, uv);   // 3 back, outer
            b.AddVertex(outAt + front, radial, uv);           // 4 outer wall, front
            b.AddVertex(outAt - front, radial, uv);           // 5 outer wall, back
            b.AddVertex(inAt + front, -radial, uv);           // 6 inner wall, front
            b.AddVertex(inAt - front, -radial, uv);           // 7 inner wall, back
        }
        for (int k = 0; k < segments; k++)
        {
            int c0 = 8 * k, c1 = 8 * ((k + 1) % columns);
            // Counter-clockwise seen from the side each face's normal points to.
            b.AddQuad(c0 + 0, c0 + 1, c1 + 1, c1 + 0);   // front (+Z): out along the radius, on round, back in
            b.AddQuad(c0 + 2, c1 + 2, c1 + 3, c0 + 3);   // back (−Z): the reverse
            b.AddQuad(c0 + 4, c0 + 5, c1 + 5, c1 + 4);   // outer wall: front to back, on round, back to front
            b.AddQuad(c0 + 6, c1 + 6, c1 + 7, c0 + 7);   // inner wall: the reverse
        }
        if (!whole)
        {
            Cap(b, inner, outer, half, fromTurn, start: true);
            Cap(b, inner, outer, half, toTurn, start: false);
        }
        return b.Build();
    }

    /// <summary>One end of an open arc: a quad across the band facing back along the turn (<paramref name="start"/>) or on along it.</summary>
    private static void Cap(MeshBuilder b, float inner, float outer, float half, float turn, bool start)
    {
        float angle = turn * MathF.Tau;
        var radial = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f);
        var tangent = new Vector3(-MathF.Sin(angle), MathF.Cos(angle), 0f);   // the way the turn goes on
        Vector3 normal = start ? -tangent : tangent;
        Vector3 inAt = radial * inner, outAt = radial * outer, front = Vector3.UnitZ * half;
        var uv = new Vector2(turn, 1f);
        int inFront = b.AddVertex(inAt + front, normal, uv);
        int inBack = b.AddVertex(inAt - front, normal, uv);
        int outBack = b.AddVertex(outAt - front, normal, uv);
        int outFront = b.AddVertex(outAt + front, normal, uv);
        if (start) b.AddQuad(inFront, inBack, outBack, outFront);
        else b.AddQuad(inFront, outFront, outBack, inBack);
    }

    private static Matrix4x4 At(float x, float y, float z) => Matrix4x4.CreateTranslation(x, y, z);

    private static Vector4 Colour(Vector3 srgb) => new(Palette.Linear(srgb), 1f);
}
