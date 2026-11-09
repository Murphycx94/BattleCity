using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The own buff layers over the status panel (stage 6 design §4.3): in <see cref="HudBoxes.Buffs"/>, from left to right,
/// an icon <see cref="IconSize"/> square for each kind the tank has a layer of — reinforced armour a shield, fast reload an
/// open ring with an arrowhead, optics a pair of binoculars seen from the front (two lenses under two eyepieces) — filling
/// about 30 of the 32 px, in the crate's colour with a dark rim, and the count ("×2", <see cref="CrateText.Layers"/>,
/// <see cref="CountSize"/> px) at its bottom right. Kinds at 0 are left out (the row closes up), so with no layers
/// nothing is drawn. Redrawn only when the layers change; the counts' words are built once. The scene hands it no
/// layers while the status panel is down (no tank, a wreck, the results), and the HUD hides it under the scoreboard.
/// </summary>
public partial class BuffRowView : HudPanel
{
    public const float IconSize = 32f;

    private const float Pitch = 76f;           // one icon and its count to the next icon
    private const float CountInset = 4f;       // the count's left end, this far inside the icon's right edge
    private const int CountSize = 20, CountOutline = 4;
    private const float Rim = 2f;              // the dark line round each icon
    private const float Stroke = 4f;           // the reload ring's width
    private const float LensStroke = 5f;       // the lenses' width
    private const int ArcSegments = 32;

    private static readonly string[] Counts = BuildCounts();
    private static readonly Color RimColor = Palette.UiOutline.ToColor();

    // The shield's outline in a 32-unit square, y down: a flat top with rounded shoulders, a point at the bottom; 1–31 tall.
    private static readonly Vector2[] ShieldShape =
    {
        new(3, 5), new(16, 1), new(29, 5), new(29, 16), new(24, 25), new(16, 31), new(8, 25), new(3, 16),
    };

    // The binoculars: two lenses (centres, radius) under two eyepieces over their centres (half width, top, bottom).
    private static readonly Vector2 LeftLens = new(10, 20), RightLens = new(22, 20);
    private const float LensRadius = 8f, GlassRadius = LensRadius - LensStroke / 2;   // the glass fills the ring's hole
    private const float EyepieceHalfWidth = 3f, EyepieceTop = 2f, EyepieceBottom = 12f;

    private readonly Vector2[] _points = new Vector2[ShieldShape.Length + 1];
    private BuffHud _buffs;

    /// <summary>Once a frame: the layers to show (all 0: none); redraws only when they changed.</summary>
    public void Update(in BuffHud buffs)
    {
        if (buffs == _buffs) return;
        _buffs = buffs;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_buffs.Any) return;
        HudRect box = Boxes.Buffs;
        var at = new Vector2(box.X, box.Y + (box.Height - IconSize) / 2);
        if (_buffs.Armor > 0) at = DrawBuff(at, CrateKind.Armor, _buffs.Armor);
        if (_buffs.Reload > 0) at = DrawBuff(at, CrateKind.Reload, _buffs.Reload);
        if (_buffs.Optics > 0) DrawBuff(at, CrateKind.Optics, _buffs.Optics);
    }

    /// <summary>One icon with its top left at <paramref name="at"/> and its count; returns where the next one goes.</summary>
    private Vector2 DrawBuff(Vector2 at, CrateKind kind, byte layers)
    {
        Color color = Palette.Crate(kind).ToColor();
        switch (kind)
        {
            case CrateKind.Armor: DrawShield(at, color); break;
            case CrateKind.Reload: DrawReload(at, color); break;
            default: DrawOptics(at, color); break;
        }
        var baseline = new Vector2(at.X + IconSize - CountInset, at.Y + IconSize);
        HudStyle.DrawOutlined(this, baseline, Counts[Math.Min((int)layers, CrateRules.MaxLayers)], CountSize, HudStyle.Text, CountOutline);
        return at + new Vector2(Pitch, 0f);
    }

    private void DrawShield(Vector2 at, Color color)
    {
        for (int i = 0; i < ShieldShape.Length; i++) _points[i] = at + ShieldShape[i];
        _points[ShieldShape.Length] = _points[0];
        DrawColoredPolygon(_points.AsSpan(0, ShieldShape.Length), color, ReadOnlySpan<Vector2>.Empty, null);
        DrawPolyline(_points, RimColor, Rim, true);
        DrawLine(at + new Vector2(16, 6), at + new Vector2(16, 26), RimColor, Rim, true);   // the boss down the middle
    }

    /// <summary>An open ring, its gap at the top, with an arrowhead on its end pointing on round the turn (clockwise).</summary>
    private void DrawReload(Vector2 at, Color color)
    {
        Vector2 centre = at + new Vector2(16, 16);
        const float radius = 13f, from = -0.35f * Mathf.Pi, to = from + 1.6f * Mathf.Pi;   // y down: growing angles run clockwise
        RimmedArc(centre, radius, from, to, color, Stroke);
        var outward = new Vector2(Mathf.Cos(to), Mathf.Sin(to));
        var along = new Vector2(-outward.Y, outward.X);   // the way the ring runs at its end
        Vector2 end = centre + radius * outward;
        _points[0] = end + along * 10f;
        _points[1] = end - along * 1f + outward * 9f;
        _points[2] = end - along * 1f - outward * 9f;
        _points[3] = _points[0];
        DrawPolyline(_points.AsSpan(0, 4), RimColor, 2 * Rim, true);
        DrawColoredPolygon(_points.AsSpan(0, 3), color, ReadOnlySpan<Vector2>.Empty, null);
    }

    /// <summary>
    /// Binoculars from the front: two eyepieces, then two lens rings over their feet — the rings' rims under both fills, so
    /// the rings merge where they overlap — and last the two dark glasses, whole, so neither ring cuts into the other's.
    /// </summary>
    private void DrawOptics(Vector2 at, Color color)
    {
        DrawEyepiece(at, LeftLens.X, color);
        DrawEyepiece(at, RightLens.X, color);
        Vector2 left = at + LeftLens, right = at + RightLens;
        DrawArc(left, LensRadius, 0f, Mathf.Tau, ArcSegments, RimColor, LensStroke + 2 * Rim, true);
        DrawArc(right, LensRadius, 0f, Mathf.Tau, ArcSegments, RimColor, LensStroke + 2 * Rim, true);
        DrawArc(left, LensRadius, 0f, Mathf.Tau, ArcSegments, color, LensStroke, true);
        DrawArc(right, LensRadius, 0f, Mathf.Tau, ArcSegments, color, LensStroke, true);
        DrawCircle(left, GlassRadius, RimColor, true, -1f, true);
        DrawCircle(right, GlassRadius, RimColor, true, -1f, true);
    }

    /// <summary>An eyepiece over the lens centred at <paramref name="x"/> across the icon: a small rimmed block from <see cref="EyepieceTop"/> down into the lens.</summary>
    private void DrawEyepiece(Vector2 at, float x, Color color)
    {
        _points[0] = at + new Vector2(x - EyepieceHalfWidth, EyepieceTop);
        _points[1] = at + new Vector2(x + EyepieceHalfWidth, EyepieceTop);
        _points[2] = at + new Vector2(x + EyepieceHalfWidth, EyepieceBottom);
        _points[3] = at + new Vector2(x - EyepieceHalfWidth, EyepieceBottom);
        _points[4] = _points[0];
        DrawColoredPolygon(_points.AsSpan(0, 4), color, ReadOnlySpan<Vector2>.Empty, null);
        DrawPolyline(_points.AsSpan(0, 5), RimColor, Rim, true);
    }

    private void RimmedArc(Vector2 centre, float radius, float from, float to, Color color, float stroke)
    {
        DrawArc(centre, radius, from, to, ArcSegments, RimColor, stroke + 2 * Rim, true);
        DrawArc(centre, radius, from, to, ArcSegments, color, stroke, true);
    }

    private static string[] BuildCounts()
    {
        var counts = new string[CrateRules.MaxLayers + 1];
        for (int n = 0; n <= CrateRules.MaxLayers; n++) counts[n] = CrateText.Layers((byte)n);
        return counts;
    }
}
