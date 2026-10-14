using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>
/// One smooth cartoon boulder per rock prop, sized to its collision box (so what you see is about what you hit) and
/// buried a little so it never floats, plus a few loose pebbles around each one in a single MultiMesh (no collision).
/// </summary>
public partial class RockMeshes : Node3D
{
    private const float BuryDepth = 0.3f;
    public const float Fit = RockShape.Fit;   // raw shape width / collision width
    private const int Subdivisions = 3;
    private const int MinPebbles = 3;
    private const int MaxPebbles = 6;
    private static readonly SN.Vector3 PebbleHalf = new(0.6f, 0.4f, 0.5f);
    private const float RimStrength = 0.25f;
    private const float PebbleBrightness = 1.0f;

    public void Build(IReadOnlyList<Prop> props, HeightField terrain)
    {
        ShaderMaterial material = Materials.FromShader("rock").With("toon_rim_strength", RimStrength);
        var pebbles = new List<Transform3D>();
        ShaderMaterial pebbleMaterial = Materials.FromShader("rock").With("brightness", PebbleBrightness);
        foreach (Prop prop in props)
        {
            if (prop.Kind != PropKind.Rock) continue;   // bushes have their own view (BushField)
            float bottom = LowestGround(prop.Footprint, terrain) - BuryDepth;
            float height = prop.Top - bottom;
            var half = new SN.Vector3(prop.Footprint.HalfExtents.X * Fit, height / 2, prop.Footprint.HalfExtents.Y * Fit);
            MeshData rock = RockShape.Build(half, (uint)prop.Id * 2654435761u, Subdivisions);
            AddChild(new MeshInstance3D
            {
                Mesh = rock.ToArrayMesh(),
                MaterialOverride = material,
                Position = prop.Footprint.Center.Ground(bottom + height / 2),
                Rotation = new Vector3(0, prop.Footprint.Yaw, 0),
            });
            ScatterPebbles(prop, terrain, pebbles);
        }

        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = RockShape.Build(PebbleHalf, 99u, 1).ToArrayMesh(),
            InstanceCount = pebbles.Count,
        };
        for (int i = 0; i < pebbles.Count; i++) multimesh.SetInstanceTransform(i, pebbles[i]);
        AddChild(new MultiMeshInstance3D { Multimesh = multimesh, MaterialOverride = pebbleMaterial });
    }

    /// <summary>Lowest ground under the footprint's centre and corners, so the rock reaches the ground on slopes.</summary>
    private static float LowestGround(Obb2 box, HeightField terrain)
    {
        float lowest = terrain.HeightAt(box.Center.X, box.Center.Y);
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
        {
            SN.Vector2 corner = box.Center + box.AxisRight * (box.HalfExtents.X * sx) + box.AxisForward * (box.HalfExtents.Y * sz);
            lowest = MathF.Min(lowest, terrain.HeightAt(corner.X, corner.Y));
        }
        return lowest;
    }

    private static void ScatterPebbles(Prop prop, HeightField terrain, List<Transform3D> into)
    {
        var rng = new XorShift((uint)prop.Id * 747796405u + 1u);
        int count = MinPebbles + (int)(rng.NextUInt() % (MaxPebbles - MinPebbles + 1));
        float reach = MathF.Max(prop.Footprint.HalfExtents.X, prop.Footprint.HalfExtents.Y);
        for (int k = 0; k < count; k++)
        {
            float angle = rng.NextFloat() * Angles.TwoPi;
            float distance = reach + 0.4f + rng.NextFloat() * 1.8f;
            float scale = 0.35f + rng.NextFloat() * 0.5f;
            float x = prop.Footprint.Center.X + MathF.Cos(angle) * distance, z = prop.Footprint.Center.Y + MathF.Sin(angle) * distance;
            var basis = Basis.FromEuler(new Vector3((rng.NextFloat() - 0.5f) * 0.6f, rng.NextFloat() * Mathf.Tau, (rng.NextFloat() - 0.5f) * 0.6f))
                .Scaled(Vector3.One * scale);
            into.Add(new Transform3D(basis, new Vector3(x, terrain.HeightAt(x, z) + PebbleHalf.Y * scale * 0.4f, z)));
        }
    }
}
