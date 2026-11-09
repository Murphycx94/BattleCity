using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Client.Match;
using Tank.Sim.Modes;
using Tank.Sim.Terrain;

namespace Tank.Client.World;

/// <summary>
/// The standard battle's two bases drawn on the ground (stage 5 design §3.4): one ring each, the own team's blue-grey
/// (<see cref="Palette.AllyBody"/>), the other's red (<see cref="Palette.EnemyBody"/>), whose team decides by the player's
/// team (a spectator's too; <see cref="MatchColors.BaseRing"/>). Each is a <see cref="RingMesh"/> band
/// (<see cref="RingBand.Around"/>: from <see cref="RingBand.Width"/> − <see cref="RingBand.OuterOver"/> inside the base's
/// edge to <see cref="RingBand.OuterOver"/> outside it) draped on the drawn ground (<see cref="TerrainMesh.VisualHeightAt"/>)
/// <see cref="Lift"/> m up, in <see cref="Segments"/> pieces (about 1 m each on the outer edge, finer than the terrain's
/// 2 m cells, so it follows the slopes without sinking in), drawn with the base_ring shader (a solid outer line
/// <see cref="RingBand.SolidLine"/> m wide, a dithered fade inwards) and casting no shadow. Built once per match: the
/// bases never move.
/// </summary>
public partial class BaseRings : Node3D
{
    public const float Lift = 0.3f;   // m: over the grass's lower blades (0.25–0.45 m); 0.15 was lost in them from a tank's height
    public const int Segments = 160;

    public void Build(IReadOnlyList<TeamBase> bases, byte ownTeam, HeightField terrain)
    {
        foreach (TeamBase b in bases)
        {
            RingBand band = RingBand.Around(b.Radius);
            MeshData ring = RingMesh.Build(b.Centre, band.Inner, band.Outer, Segments, (x, z) => TerrainMesh.VisualHeightAt(terrain, x, z), Lift);
            ShaderMaterial material = Materials.FromShader("base_ring")
                .With("ring_color", MatchColors.BaseRing(ownTeam, b.Team).ToColor())
                .With("solid_from", band.SolidFrom);
            AddChild(new MeshInstance3D
            {
                Name = $"Base{b.Team}",
                Mesh = ring.ToArrayMesh(),
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }
}
