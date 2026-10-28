using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;
using SN = System.Numerics;
using Side = Tank.Client.Match.Side;

namespace Tank.Client.Minimap;

/// <summary>
/// One map on the screen (stage 5 design §5): the corner map or the full-screen one, filling its own rectangle. Draws, in
/// this order, a rounded dark box, the map's picture inset in it (nothing more until the ground is baked), the bases'
/// tinted discs, rings and progress arcs (outside the ring), the view fan, the wrecks' crosses, the teammates, the enemies, the own wreck and the eye
/// (an arrow, or a ring). A point on the ground (x, z) is at <c>inner.Position + area.ToUnit(ground) · inner.Size</c>;
/// icons beyond the box are cut at its edge (<see cref="Control.ClipContents"/>). Click-through. Redraws every frame it
/// is given one (the icons move); <see cref="_Draw"/> allocates nothing: the polygons go through one preallocated buffer.
/// </summary>
public partial class MinimapView : Control
{
    private const int CornerDetail = 6;
    private const int CornerRadius = 10, FullRadius = 14;
    private const float BoxAlpha = 0.85f;
    private const float PictureInset = 4f;
    private const int BaseRingSegments = 48;
    private const int EyeRingSegments = 32;

    private const int BufferPoints = MinimapGlyphs.ArcMaxPoints + 1;   // the longest polygon, and room to close an outline

    private readonly SN.Vector2[] _glyph = new SN.Vector2[BufferPoints];   // MinimapGlyphs writes here …
    private readonly Vector2[] _points = new Vector2[BufferPoints];        // … and the points are copied here for the drawing calls

    private static readonly Color Outline = MinimapStyle.IconOutline.ToColor();
    private static readonly Color EyeColor = MinimapStyle.Eye.ToColor();
    private static readonly Color FanColor = MinimapStyle.Fan.ToColor(MinimapStyle.FanOpacity);

    private StyleBoxFlat _box = null!;
    private MinimapIconSizes _sizes;
    private Texture2D? _texture;
    private MinimapArea _area;
    private MinimapFrame? _frame;
    private Rect2 _inner;   // where the picture lies, and the ground square with it

    /// <summary>Picks the corner map's or the full map's icon sizes and corner radius. Call before adding to the tree.</summary>
    public void Setup(bool full)
    {
        _sizes = MinimapStyle.Icons(full);
        _box = HudStyle.RoundedBox(Palette.UiPanel.ToColor(BoxAlpha), full ? FullRadius : CornerRadius, CornerDetail);
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;   // 1024 pixels drawn in 232 without shimmering
    }

    /// <summary>What to draw this frame: the picture (null until the ground is baked), the square it covers, the icons.</summary>
    public void Show(Texture2D? texture, MinimapArea area, MinimapFrame frame)
    {
        (_texture, _area, _frame) = (texture, area, frame);
        QueueRedraw();
    }

    /// <summary>Lets go of the picture (its owner is about to free it); the box and icons stay until the next <see cref="Show"/>.</summary>
    public void DropTexture()
    {
        _texture = null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawStyleBox(_box, rect);
        _inner = rect.Grow(-PictureInset);
        if (_texture is not null) DrawTextureRect(_texture, _inner, false);
        if (_frame is null || _area.Size <= 0) return;

        foreach (MinimapBase b in _frame.Bases) DrawBase(b);
        DrawFan(_frame.Eye);
        foreach (MinimapTank t in _frame.Tanks)
            if (t.Destroyed) DrawCross(ToScreen(t.Position), ToColor(MinimapStyle.TankColor(t.Side, destroyed: true)));
        foreach (MinimapTank t in _frame.Tanks)
            if (!t.Destroyed && t.Side != Side.Enemy) DrawTank(t);
        foreach (MinimapTank t in _frame.Tanks)
            if (!t.Destroyed && t.Side == Side.Enemy) DrawTank(t);
        if (_frame.OwnWreck is { } wreck) DrawCross(ToScreen(wreck), EyeColor);
        DrawEye(_frame.Eye);
    }

    private SN.Vector2 ToScreen(SN.Vector2 ground)
    {
        SN.Vector2 unit = _area.ToUnit(ground);
        return new SN.Vector2(_inner.Position.X + unit.X * _inner.Size.X, _inner.Position.Y + unit.Y * _inner.Size.Y);
    }

    /// <summary>
    /// A base: a disc tinted in its ring's colour (whose base), the ring, and the progress arc just outside the ring
    /// (<see cref="MinimapIconSizes.ArcRadius"/>), so the arc never covers the ring.
    /// </summary>
    private void DrawBase(in MinimapBase b)
    {
        SN.Vector2 centre = ToScreen(b.Centre);
        Vector2 at = ToGodot(centre);
        float radius = MathF.Max(b.Radius / _area.Size * _inner.Size.X, _sizes.BaseMinRadius);
        SN.Vector3 ring = MinimapStyle.BaseRing(b.Own);
        DrawCircle(at, _sizes.FillRadius(radius), ring.ToColor(MinimapStyle.BaseFillOpacity), true, -1, true);
        DrawArc(at, radius, 0, Mathf.Tau, BaseRingSegments, ring.ToColor(), _sizes.RingWidth, true);
        int n = MinimapGlyphs.Arc(centre, _sizes.ArcRadius(radius), b.Points, _glyph);
        if (n >= 2) DrawPolyline(Copy(n), MinimapStyle.BaseArc(b.Own).ToColor(), _sizes.ArcWidth, true);
    }

    private void DrawFan(in MinimapEye eye)
    {
        if (eye.Kind == EyeKind.None) return;
        int n = MinimapGlyphs.Fan(ToScreen(eye.Position), eye.CameraYaw, eye.HalfFov, _sizes.FanLength, _glyph);
        DrawColoredPolygon(Copy(n), FanColor, ReadOnlySpan<Vector2>.Empty, null);
    }

    private void DrawCross(SN.Vector2 centre, Color color)
    {
        var (a, b, c, d) = MinimapGlyphs.Cross(centre, _sizes.Cross);
        var outline = new Color(Outline, color.A);
        float rim = _sizes.CrossWidth + 2 * _sizes.OutlineWidth;
        DrawLine(ToGodot(a), ToGodot(b), outline, rim, true);
        DrawLine(ToGodot(c), ToGodot(d), outline, rim, true);
        DrawLine(ToGodot(a), ToGodot(b), color, _sizes.CrossWidth, true);
        DrawLine(ToGodot(c), ToGodot(d), color, _sizes.CrossWidth, true);
    }

    private void DrawTank(in MinimapTank t)
    {
        int n = MinimapGlyphs.Shape(t.Shape, ToScreen(t.Position), _sizes.Tank, _glyph);
        DrawFilled(n, ToColor(MinimapStyle.TankColor(t.Side, destroyed: false)));
    }

    private void DrawEye(in MinimapEye eye)
    {
        switch (eye.Kind)
        {
            case EyeKind.Arrow:
                DrawFilled(MinimapGlyphs.Arrow(ToScreen(eye.Position), eye.Yaw, _sizes.Arrow, _glyph), EyeColor);
                break;
            case EyeKind.Ring:
                Vector2 centre = ToGodot(ToScreen(eye.Position));
                float radius = _sizes.Arrow * MinimapStyle.EyeRingShare;
                DrawArc(centre, radius, 0, Mathf.Tau, EyeRingSegments, Outline, _sizes.EyeRingWidth + 2 * _sizes.OutlineWidth, true);
                DrawArc(centre, radius, 0, Mathf.Tau, EyeRingSegments, EyeColor, _sizes.EyeRingWidth, true);
                break;
        }
    }

    /// <summary>The <paramref name="n"/> points in the glyph buffer as a polygon filled with <paramref name="color"/> and rimmed in the outline colour.</summary>
    private void DrawFilled(int n, Color color)
    {
        DrawColoredPolygon(Copy(n), color, ReadOnlySpan<Vector2>.Empty, null);
        _points[n] = _points[0];   // closed
        DrawPolyline(_points.AsSpan(0, n + 1), new Color(Outline, color.A), _sizes.OutlineWidth, true);
    }

    /// <summary>The first <paramref name="n"/> glyph points as Godot vectors.</summary>
    private ReadOnlySpan<Vector2> Copy(int n)
    {
        for (int i = 0; i < n; i++) _points[i] = ToGodot(_glyph[i]);
        return _points.AsSpan(0, n);
    }

    private static Vector2 ToGodot(SN.Vector2 v) => new(v.X, v.Y);

    private static Color ToColor(SN.Vector4 rgba) => new(rgba.X, rgba.Y, rgba.Z, rgba.W);
}
