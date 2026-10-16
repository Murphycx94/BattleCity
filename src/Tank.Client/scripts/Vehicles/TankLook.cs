using SN = System.Numerics;

namespace Tank.Client.Vehicles;

public enum DetailKind : byte
{
    Headlight, // round lamp, axis along the part's local Z, lens towards −Z; painted in the hub colour
    Cupola,    // round hatch: a short round tower with a domed lid
}

/// <summary>
/// A small add-on part. Position is the part's centre (Cupola: base centre) in hull space, or turret space when
/// <see cref="OnTurret"/>. Size is (diameter, diameter, length) for a Headlight, (diameter, height, —) for a Cupola.
/// </summary>
public readonly record struct DetailPart(DetailKind Kind, SN.Vector3 Position, SN.Vector3 Size, bool OnTurret = false);

/// <summary>A wheel in the side view: Z along the hull, Y height of the centre, Radius.</summary>
public readonly record struct WheelSpot(float Z, float Y, float Radius);

/// <summary>Plain toy paint, sRGB 0–1 (normally palette entries): body; tracks, tyres and muzzle bulb; wheel hubs and headlights.</summary>
public readonly record struct TankPaint(SN.Vector3 Body, SN.Vector3 Track, SN.Vector3 Hub);

/// <summary>
/// Everything about how a vehicle looks, one per <c>TankSpec</c>. Hull space: origin at the ground contact centre,
/// +Y up, −Z forward. Profiles are convex; side profiles are (z, y).
/// Engine-neutral on purpose, so Tank.Art.Tests can build and check every look.
/// </summary>
public sealed record TankLook
{
    public required string SpecId { get; init; }

    /// <summary>Lower hull between the tracks: rounded side profile.</summary>
    public required SN.Vector2[] LowerHull { get; init; }
    public required float LowerHullWidth { get; init; }

    /// <summary>Upper hull spanning the tracks: rounded side profile.</summary>
    public required SN.Vector2[] UpperHull { get; init; }
    public required float UpperHullWidth { get; init; }

    /// <summary>Radii the long side edges of each hull layer are rounded over (see <c>RoundedSlab</c>).</summary>
    public required float LowerHullRound { get; init; }
    public required float UpperHullRound { get; init; }

    /// <summary>
    /// Hull-space height of the turret's flat bottom (a little below the roof, so no seam shows). Without a turret, the
    /// height where the gun mount rides the hull's nod (the trunnion's).
    /// </summary>
    public required float TurretBase { get; init; }

    /// <summary>
    /// Dome profile (radius, height above <see cref="TurretBase"/>) for <c>Lathe</c>, bottom to top, closed at the top;
    /// empty for a casemate (no turret: the upper hull is the casemate and only the mantlet and barrel turn).
    /// </summary>
    public required SN.Vector2[] TurretProfile { get; init; }

    /// <summary>False for a casemate tank (an empty <see cref="TurretProfile"/>): the upper hull is the casemate and only the mantlet and barrel turn.</summary>
    public bool HasTurret => TurretProfile.Length > 0;

    /// <summary>
    /// The turret's cross-section (a <c>Superellipse</c> exponent): 2 sweeps <see cref="TurretProfile"/> round into a dome,
    /// more squares it off into a rounded block whose sides sit at the profile's radius.
    /// </summary>
    public float TurretSquareness { get; init; } = 2f;

    /// <summary>
    /// Turret-space Z of the gun's visual pivot (the mantlet centre). The barrel is shortened by the same amount, so at
    /// zero pitch the muzzle sits exactly where the sim puts it.
    /// </summary>
    public required float GunPivotZ { get; init; }

    /// <summary>The mantlet is a capsule across the gun (axis X) centred on the pivot, so it looks the same at every pitch.</summary>
    public required float MantletRadius { get; init; }
    public required float MantletWidth { get; init; }

    public required float BarrelRadius { get; init; }

    /// <summary>The round bulb at the muzzle end: its radius and length along the barrel.</summary>
    public required float MuzzleRadius { get; init; }
    public required float MuzzleLength { get; init; }

    public required float TrackX { get; init; }
    public required float TrackWidth { get; init; }
    public required float TrackThickness { get; init; }
    public required float LinkPitch { get; init; }
    public required WheelSpot Sprocket { get; init; }
    public required WheelSpot Idler { get; init; }
    public required int RoadWheelCount { get; init; }
    public required float RoadWheelRadius { get; init; }
    public required float RoadWheelY { get; init; }
    public required float RoadWheelSpacing { get; init; }
    public required float RoadWheelWidth { get; init; }

    public required DetailPart[] Details { get; init; }

    public required TankPaint Paint { get; init; }

    public float RoadWheelZ(int index) => (index - (RoadWheelCount - 1) / 2f) * RoadWheelSpacing;
}
