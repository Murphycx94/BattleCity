using System.Runtime.InteropServices;
using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Terrain;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>
/// The zone's light wall in the match scene (stage 6 design §3.3): one <see cref="ZoneWallMesh"/> cylinder the zone_wall
/// shader bends onto the safe circle every frame (<see cref="Show"/> sets the centre and the radius, nothing else), so
/// a shrinking ring costs two uniforms a frame. Its columns stand on the drawn ground, or the water over it
/// (<see cref="TerrainMesh.VisualHeightAt"/>, like the base rings), sampled by <see cref="ZoneWallShape.SampleBase"/> into
/// a 256 × 1 float texture only when the circle has moved more than <see cref="ResampleStep"/> since the last sample.
/// Built once a match (<see cref="Build"/>): the mesh, the material, the texture and the buffers behind it are all
/// preallocated, so the per-frame path allocates nothing managed. Hidden below <see cref="ZoneWallShape.MinRadius"/>
/// (the zone closed). Its bounds are set by hand (the vertices move in the shader): ring 0's circle, 50 m under the ground
/// to 300 m over it.
/// </summary>
public partial class ZoneWall : Node3D
{
    /// <summary>m: the base heights are sampled again once the centre or the radius has moved this far.</summary>
    public const float ResampleStep = 0.5f;

    private const float BoundsBelow = 50f, BoundsAbove = 300f;
    private static readonly StringName CentreParam = "centre", RadiusParam = "radius";

    private readonly float[] _heights = new float[ZoneWallShape.Segments];
    private readonly byte[] _pixels = new byte[ZoneWallShape.Segments * sizeof(float)];
    private MeshInstance3D _mesh = null!;
    private ShaderMaterial _material = null!;
    private Image _image = null!;
    private ImageTexture _texture = null!;
    private Func<float, float, float> _groundAt = null!;
    private ZoneRing _sampled;   // the circle the heights were last sampled on
    private bool _sampledOnce;
    private bool _shown;

    /// <summary>The mesh, the material and the 256 × 1 base texture, for <paramref name="field"/>'s ground and water.</summary>
    public void Build(Battlefield field)
    {
        HeightField terrain = field.Terrain;
        _groundAt = field.WaterLevel is { } water
            ? (x, z) => MathF.Max(TerrainMesh.VisualHeightAt(terrain, x, z), water)
            : (x, z) => TerrainMesh.VisualHeightAt(terrain, x, z);
        _image = Image.CreateFromData(ZoneWallShape.Segments, 1, false, Image.Format.Rf, _pixels);
        _texture = ImageTexture.CreateFromImage(_image);
        _material = Materials.FromShader("zone_wall")
            .With("height", ZoneWallShape.Height)
            .With("sink", ZoneWallShape.Sink)
            .With("base_heights", _texture)
            .With("wall_color", Palette.ZoneWall.ToColor());
        ZoneRing outer = ZoneSchedule.Outer(field);
        _mesh = new MeshInstance3D
        {
            Name = "Wall",
            Mesh = ZoneWallMesh.Build(ZoneWallShape.Segments).ToArrayMesh(),
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(
                new Vector3(outer.Centre.X - outer.Radius, -BoundsBelow, outer.Centre.Y - outer.Radius),
                new Vector3(2f * outer.Radius, BoundsBelow + BoundsAbove, 2f * outer.Radius)),
            Visible = false,
        };
        AddChild(_mesh);
    }

    /// <summary>
    /// Once a frame: the wall on <paramref name="circle"/> (hidden below <see cref="ZoneWallShape.MinRadius"/>); the base
    /// heights sampled again when the circle moved more than <see cref="ResampleStep"/>.
    /// </summary>
    public void Show(in ZoneRing circle)
    {
        bool shown = circle.Radius >= ZoneWallShape.MinRadius;
        if (shown != _shown)
        {
            _shown = shown;
            _mesh.Visible = shown;
        }
        if (!shown) return;
        if (!_sampledOnce || Moved(circle)) Resample(circle);
        _material.SetShaderParameter(CentreParam, new Vector2(circle.Centre.X, circle.Centre.Y));
        _material.SetShaderParameter(RadiusParam, circle.Radius);
    }

    private bool Moved(in ZoneRing circle) =>
        SN.Vector2.Distance(circle.Centre, _sampled.Centre) > ResampleStep || MathF.Abs(circle.Radius - _sampled.Radius) > ResampleStep;

    private void Resample(in ZoneRing circle)
    {
        ZoneWallShape.SampleBase(_groundAt, circle, _heights);
        MemoryMarshal.AsBytes<float>(_heights).CopyTo(_pixels);   // Rf: one little-endian float a texel, as the CPU lays them out
        _image.SetData(ZoneWallShape.Segments, 1, false, Image.Format.Rf, _pixels);
        _texture.Update(_image);
        _sampled = circle;
        _sampledOnce = true;
    }
}
