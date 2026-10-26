using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Client.World;

/// <summary>
/// A deep forest band 8–90 m outside the playable area: a jittered grid of candidate spots thinned by a grove noise
/// (so trees clump into stands with clearings between them) and by depth (dense at the inner edge, ragged at the
/// outer). The band is split into square sectors, each with its own MultiMeshes per kind and variant, so frustum
/// culling drops whole sectors, and each sector fades out beyond <see cref="VisibleRange"/>. Spots on the map that are
/// under water or on a road stay empty (a river or road runs out through the band). No collision.
/// </summary>
public partial class TreeLine : Node3D
{
    private const float Inner = 8f;
    private const float Outer = 90f;
    private const float Spacing = 5.5f;
    private const float GroveScale = 1f / 38f;
    private const float ConiferShare = 0.65f;
    private const int Variants = 2;
    private const float SectorSize = 80f;
    private const float VisibleRange = 520f;
    private const float VisibleFade = 60f;
    private const uint Seed = 4242u;

    public int TreeCount { get; private set; }
    public int SectorCount { get; private set; }

    /// <summary>
    /// With <paramref name="far"/> every sector is drawn at any distance (the top-down map view); else each fades out
    /// beyond <see cref="VisibleRange"/>, as built.
    /// </summary>
    public void SetFarView(bool far)
    {
        foreach (Node child in GetChildren())
            if (child is MultiMeshInstance3D sector) sector.VisibilityRangeEnd = far ? 0f : VisibleRange;   // 0: no end
    }

    public void Build(Battlefield field)
    {
        var rng = new XorShift(Seed);
        float lo = field.Border, hiX = field.Terrain.SizeX - field.Border, hiZ = field.Terrain.SizeZ - field.Border;
        var sectors = new Dictionary<(int X, int Z), List<(Transform3D, Color)>[]>();
        float originX = lo - Outer, originZ = lo - Outer;
        for (float gz = originZ; gz < hiZ + Outer; gz += Spacing)
        for (float gx = originX; gx < hiX + Outer; gx += Spacing)
        {
            float x = gx + (rng.NextFloat() - 0.5f) * Spacing * 1.1f, z = gz + (rng.NextFloat() - 0.5f) * Spacing * 1.1f;
            float keep = rng.NextFloat(), yaw = rng.NextFloat() * Mathf.Tau, sizeRoll = rng.NextFloat(), kindRoll = rng.NextFloat();
            int variant = (int)(rng.NextFloat() * Variants) % Variants;
            var data = new Color(rng.NextFloat(), rng.NextFloat(), rng.NextFloat(), 0);
            float dx = MathF.Max(MathF.Max(lo - x, x - hiX), 0f), dz = MathF.Max(MathF.Max(lo - z, z - hiZ), 0f);
            float outside = MathF.Sqrt(dx * dx + dz * dz);
            if (outside < Inner || outside > Outer) continue;
            float depth = (outside - Inner) / (Outer - Inner);
            float grove = Mathf.Clamp(SeededNoise.Fbm(new System.Numerics.Vector3(x, 0, z) * GroveScale, Seed, 3) * 1.8f - 0.2f, 0f, 1f);
            float density = Mathf.Lerp(1.0f, 0.5f, depth) * Mathf.Lerp(0.3f, 1f, grove);
            if (keep > density) continue;
            float scale = 0.55f + 1.25f * sizeRoll * sizeRoll + 0.25f * grove;   // many mid-size, a few giants
            bool onMap = x >= 0 && z >= 0 && x <= field.Terrain.SizeX && z <= field.Terrain.SizeZ;
            // No trees in the water or on a road leaving the map. Decided after every draw above, so a map without either
            // (the sandbox) keeps exactly the same random sequence and trees.
            if (onMap && OnWaterOrRoad(field, x, z)) continue;
            float ground = onMap ? TerrainMesh.VisualHeightAt(field.Terrain, x, z) : TerrainMesh.FarGroundY;
            var transform = new Transform3D(new Basis(Vector3.Up, yaw).Scaled(new Vector3(1f, 0.9f + 0.25f * data.B, 1f) * scale), new Vector3(x, ground - 0.1f, z));
            var key = ((int)MathF.Floor((x - originX) / SectorSize), (int)MathF.Floor((z - originZ) / SectorSize));
            if (!sectors.TryGetValue(key, out var lists))
                sectors[key] = lists = Enumerable.Range(0, 2 * Variants).Select(_ => new List<(Transform3D, Color)>()).ToArray();
            lists[(kindRoll < ConiferShare ? 0 : Variants) + variant].Add((transform, data));
        }

        ShaderMaterial material = Materials.FromShader("tree");
        Mesh[] meshes = Enumerable.Range(0, Variants).Select(v => TreeShapes.Conifer(7u + (uint)v * 13u).ToArrayMesh())
            .Concat(Enumerable.Range(0, Variants).Select(v => TreeShapes.Broadleaf(11u + (uint)v * 17u).ToArrayMesh())).ToArray();
        foreach (var (key, lists) in sectors.OrderBy(entry => entry.Key))
        {
            for (int m = 0; m < lists.Length; m++)
            {
                if (lists[m].Count == 0) continue;
                AddChild(Instances(meshes[m], lists[m], material));
                TreeCount += lists[m].Count;
            }
            SectorCount++;
        }
    }

    private static bool OnWaterOrRoad(Battlefield field, float x, float z)
    {
        var ground = new System.Numerics.Vector2(x, z);
        return field.WaterDepthAt(ground) > 0f || field.SurfaceAt(ground) == SurfaceKind.Road;
    }

    private static MultiMeshInstance3D Instances(Mesh mesh, List<(Transform3D Transform, Color Data)> trees, Material material)
    {
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = mesh,
            InstanceCount = trees.Count,
        };
        for (int i = 0; i < trees.Count; i++)
        {
            multimesh.SetInstanceTransform(i, trees[i].Transform);
            multimesh.SetInstanceCustomData(i, trees[i].Data);
        }
        return new MultiMeshInstance3D
        {
            Multimesh = multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,   // far beyond the shadow range anyway
            VisibilityRangeEnd = VisibleRange,
            VisibilityRangeEndMargin = VisibleFade,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        };
    }
}
