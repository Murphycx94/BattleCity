using System.Numerics;
using Tank.Client.Art;
using Tank.Client.Match;
using Tank.Sim.Maps;

namespace Tank.Client.Minimap;

/// <summary>
/// One map's icon sizes in base pixels of the map view: a tank shape's and a wreck cross's extent (see
/// <see cref="MinimapGlyphs"/>), the eye arrow's length, the view fan's radius, and the smallest radius a base's ring is
/// drawn with (a base smaller than that on the map is drawn at it); then the line widths: a wreck cross's strokes, a
/// base's ring and its progress arc, the dark rim round every icon, the eye's ring, the zone's safe circle and its next ring.
/// </summary>
public readonly record struct MinimapIconSizes(float Tank, float Arrow, float Cross, float FanLength, float BaseMinRadius,
    float CrossWidth, float RingWidth, float ArcWidth, float OutlineWidth, float EyeRingWidth, float ZoneWidth = 2f, float NextZoneWidth = 1.5f)
{
    /// <summary>The gap between a base's ring and its progress arc, pixels.</summary>
    public const float ArcGap = 0.5f;

    /// <summary>Where a base's progress arc runs for a ring of <paramref name="ringRadius"/>: just outside the ring, never over it.</summary>
    public float ArcRadius(float ringRadius) => ringRadius + RingWidth / 2 + ArcGap + ArcWidth / 2;

    /// <summary>The radius of a base's tinted disc inside a ring of <paramref name="ringRadius"/>: up to the ring's inner edge.</summary>
    public float FillRadius(float ringRadius) => ringRadius - RingWidth / 2;
}

/// <summary>
/// How the map is drawn (stage 5 design §5): every colour comes from <see cref="Palette"/> (sRGB 0–1); this holds only the
/// blend factors, the light, the colours mixed from palette entries and the icons' sizes.
/// </summary>
public static class MinimapStyle
{
    /// <summary>The grass layer's colour between <see cref="Palette.GrassDark"/> and <see cref="Palette.GrassLight"/>: the terrain shader's <c>grass_tone</c>.</summary>
    public const float GrassTone = 0.55f;

    /// <summary>The grass as the terrain shader draws its grass layer.</summary>
    public static readonly Vector3 Grass = Vector3.Lerp(Palette.GrassDark, Palette.GrassLight, GrassTone);

    // The forest: a mix of the conifer and broadleaf greens, darkened so a wood stands out from the grass round it.
    public const float ForestLeafShare = 0.4f;
    public const float ForestDarken = 0.78f;
    public static readonly Vector3 Forest = Vector3.Lerp(Palette.Needles, Palette.Leaves, ForestLeafShare) * ForestDarken;

    /// <summary>How far full crown cover (a density of 1 or more) takes a pixel towards <see cref="Forest"/>.</summary>
    public const float ForestMix = 0.8f;

    /// <summary>One tree's crown adds this much density at its centre, falling linearly to <see cref="CrownEdgeDensity"/> at its rim (0 beyond).</summary>
    public const float CrownCentreDensity = 1f, CrownEdgeDensity = 0.3f;

    /// <summary>
    /// The canopy closes over gaps between crowns up to about twice this wide, m: the crowns' pixels are closed by a disc of
    /// this radius (3 pixels on the 1024-pixel riverland), so a wood reads as one canopy rather than green dots on earth.
    /// </summary>
    public const float CanopyGapMetres = 3.3f;

    /// <summary>The forest cover a pixel under the closed canopy gets at least (between crowns, where their density is low or 0).</summary>
    public const float CanopyCover = 0.85f;

    /// <summary>A colour channel (0–1, clamped) as a byte of the picture.</summary>
    internal static byte ToByte(float channel) => (byte)MathF.Round(Math.Clamp(channel, 0f, 1f) * byte.MaxValue);

    /// <summary>The hillshade's light, towards the sun: from the north-west (−x, −z) and above.</summary>
    public static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-1f, 1.6f, -1f));

    /// <summary>Shade = <see cref="ShadeAmbient"/> + <see cref="ShadeDiffuse"/> · max(0, n · L): ground facing away from the light is not black.</summary>
    public const float ShadeAmbient = 0.72f, ShadeDiffuse = 0.42f;

    /// <summary>The highest ground on the map is this much brighter, the lowest this much darker (linear in between).</summary>
    public const float HeightShade = 0.06f;

    /// <summary>Water runs from <see cref="Palette.WaterShallow"/> to <see cref="Palette.WaterDeep"/> as the depth goes from the first to the second, m.</summary>
    public const float ShallowDepth = 0.3f, DeepDepth = 3f;

    /// <summary>Water shallower than this (m) is the shore line: <see cref="ShoreFoam"/> of <see cref="Palette.Foam"/> mixed in.</summary>
    public const float ShoreBand = 0.3f, ShoreFoam = 0.5f;

    // Houses and walls (MinimapImage): standing ones solid, fallen ones a pale wash of plaster over the ground.

    /// <summary>A standing house's roof, and the 1-pixel rim round it: <see cref="HouseOutlineMix"/> of the outline colour over the roof.</summary>
    public static readonly Vector3 HouseRoof = Palette.RoofTile;
    public const float HouseOutlineMix = 0.6f;
    public static readonly Vector3 HouseRim = Vector3.Lerp(HouseRoof, Palette.UiOutline, HouseOutlineMix);

    /// <summary>What fallen masonry leaves: this much of <see cref="Rubble"/> over the ground, a house's footprint or a wall's strip.</summary>
    public static readonly Vector3 Rubble = Palette.Plaster;
    public const float HouseRubbleMix = 0.45f, WallRubbleMix = 0.35f;

    /// <summary>A wall is drawn at least this many pixels thick (a 0.6 m wall is thinner than a pixel of the map).</summary>
    public const int WallMinPixels = 2;

    /// <summary>A standing wall's colour, unmixed: stone grey or brick red.</summary>
    public static Vector3 Wall(PropKind kind) => kind == PropKind.StoneWall ? Palette.Stone : Palette.Brick;

    /// <summary>
    /// The colour of bare ground of a kind: grass cells' bare ground is the sandbox's light dirt (<see cref="Palette.Dirt"/>,
    /// what shows through between grass patches), the other kinds as the terrain shader's layered mode paints them.
    /// </summary>
    public static Vector3 BareGround(SurfaceKind kind) => kind switch
    {
        SurfaceKind.Road => Palette.Road,
        SurfaceKind.Sand => Palette.Sand,
        SurfaceKind.Dirt => Palette.Earth,
        _ => Palette.Dirt,
    };

    // Icons (MinimapFrame, drawn by the view): sizes per map, colours as RGB plus an opacity.

    /// <summary>The corner map's icons: big enough to tell the four shapes apart at 1×.</summary>
    public static readonly MinimapIconSizes CornerIcons = new(Tank: 10, Arrow: 15, Cross: 9, FanLength: 40, BaseMinRadius: 9,
        CrossWidth: 2.5f, RingWidth: 2.5f, ArcWidth: 3, OutlineWidth: 1.5f, EyeRingWidth: 2);

    /// <summary>The full-screen map's icons.</summary>
    public static readonly MinimapIconSizes FullIcons = new(Tank: 15, Arrow: 22, Cross: 13, FanLength: 90, BaseMinRadius: 12,
        CrossWidth: 3, RingWidth: 3, ArcWidth: 4, OutlineWidth: 2, EyeRingWidth: 2.5f, ZoneWidth: 3, NextZoneWidth: 2);

    public static MinimapIconSizes Icons(bool full) => full ? FullIcons : CornerIcons;

    /// <summary>Every icon (the tanks, the crosses, the eye) is rimmed <see cref="MinimapIconSizes.OutlineWidth"/> wide in this.</summary>
    public static readonly Vector3 IconOutline = Palette.UiOutline;

    /// <summary>The eye's ring (the free camera, the wreck's close-up): this share of the arrow's length as its radius.</summary>
    public const float EyeRingShare = 0.5f;

    /// <summary>A base's disc inside its ring, in the ring's colour at this opacity: whose base it is, at a glance.</summary>
    public const float BaseFillOpacity = 0.25f;

    /// <summary>The opacity of an enemy wreck's cross and of the view fan.</summary>
    public const float EnemyWreckOpacity = 0.55f, FanOpacity = 0.30f;

    /// <summary>The eye (arrow or ring) and the own wreck's cross: the own tank's orange.</summary>
    public static readonly Vector3 Eye = Palette.TankBody;

    /// <summary>The view fan's colour, drawn at <see cref="FanOpacity"/>.</summary>
    public static readonly Vector3 Fan = Palette.UiText;

    /// <summary>
    /// A tank icon's colour and opacity: the own orange, teammates blue-grey, enemies red; a teammate's wreck in the dim
    /// grey, an enemy's in its red at <see cref="EnemyWreckOpacity"/>, the own wreck orange.
    /// </summary>
    public static Vector4 TankColor(Side side, bool destroyed)
    {
        Vector3 rgb = destroyed && side == Side.Ally ? Palette.UiDim : MatchColors.Body(side);
        float opacity = destroyed && side == Side.Enemy ? EnemyWreckOpacity : 1f;
        return new Vector4(rgb, opacity);
    }

    // The zone (stage 6 design §3.3): the safe circle's line in the wall's blue, the next ring in white, the ground outside darkened.

    /// <summary>The safe circle's line: the wall's colour.</summary>
    public static readonly Vector3 ZoneLine = Palette.ZoneWall;

    /// <summary>The next ring's line.</summary>
    public static readonly Vector3 NextZoneLine = Palette.UiText;

    /// <summary>The ground outside the safe circle: this colour over it at <see cref="ZoneShadeOpacity"/>.</summary>
    public static readonly Vector3 ZoneShade = Palette.UiBackground;
    public const float ZoneShadeOpacity = 0.35f;

    /// <summary>A base's ring: the own team's in the ally blue-grey, the other's in the enemy red, as on the ground.</summary>
    public static Vector3 BaseRing(bool own) => MatchColors.Body(own ? Side.Ally : Side.Enemy);

    /// <summary>
    /// A base's progress arc, coloured as its capture bar under the plate (never the blink's dim half): the own base being
    /// taken in danger red, the enemy's in the ally blue-grey.
    /// </summary>
    public static Vector3 BaseArc(bool own) => MatchColors.FillColor(MatchColors.FillOf(new CaptureBar(own, 0), blinkDark: false));
}
