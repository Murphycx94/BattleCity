using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>
/// Draws every bush prop with one MultiMesh per mesh variant (bush.gdshader), each instance scaled to its prop's radius
/// and height. Every frame it works out, per bush:
/// - opacity: bushes within <see cref="NearCamera"/> of the camera, bushes wrapping the player's own tank (hull grown
///   by <see cref="OwnTankMargin"/>) and bushes standing between the camera and that tank (2D segment test), fade to <see cref="SeeThrough"/> over <see cref="FadeSeconds"/> so they never hide
///   your own view; the shader does it with screen-door dithering, so nothing needs sorting;
/// - push: while the tank overlaps a bush its leaves part around the hull; after it leaves they spring back over
///   <see cref="ReboundSeconds"/> (the shader pushes away from the last overlapping tank position).
/// Only instances whose values changed are re-uploaded. A bush shot away is scaled to nothing until it is restored
/// (<see cref="SyncStanding"/>), and skipped meanwhile: its fade and push stay where they were.
/// </summary>
public partial class BushField : Node3D
{
    public const float NearCamera = 6f;
    public const float OwnTankMargin = 1f;
    public const float SeeThrough = 0.5f;
    public const float FadeSeconds = 0.3f;
    public const float ReboundSeconds = 0.5f;
    private const float PushMargin = 0.4f;     // a bush starts parting this far before the hull touches its edge
    private const float GrowSeconds = 0.12f;   // how fast the leaves get out of the way
    private const int Variants = 4;
    private const float Sink = 0.15f;          // roots a little below the ground on uneven terrain

    private readonly List<Bush> _bushes = new();

    private sealed class Bush
    {
        public required int Id { get; init; }
        public required MultiMesh Multimesh { get; init; }
        public required int Index { get; init; }
        public required Transform3D Transform { get; init; }
        public required SN.Vector2 Center { get; init; }
        public required float Radius { get; init; }
        public required float Bottom { get; init; }
        public required float Top { get; init; }
        public float Opacity = 1f;
        public float Push;
        public SN.Vector2 PushFrom;
        public Color Uploaded = new(1f, 0f, 0f, 0f);
        public bool Shown = true;
    }

    public int Count => _bushes.Count;

    public void Build(IReadOnlyList<Prop> props, HeightField terrain)
    {
        Prop[] bushes = props.Where(p => p.Kind == PropKind.Bush).ToArray();
        ShaderMaterial material = Materials.FromShader("bush");
        ShaderMaterial shadowMaterial = Materials.FromShader("bush").With("dither", false);
        for (int v = 0; v < Variants; v++)
        {
            Prop[] mine = bushes.Where((_, i) => i % Variants == v).ToArray();
            if (mine.Length == 0) continue;
            var multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = BushShape.Build(31u + (uint)v * 977u).ToArrayMesh(),
                InstanceCount = mine.Length,
            };
            for (int i = 0; i < mine.Length; i++)
            {
                Prop prop = mine[i];
                float ground = terrain.HeightAt(prop.Footprint.Center.X, prop.Footprint.Center.Y) - Sink;
                float radius = prop.Footprint.HalfExtents.X;
                float yaw = (prop.Id * 0.6180339f % 1f) * Mathf.Tau;
                var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(radius, prop.Top - ground, radius));
                var transform = new Transform3D(basis, prop.Footprint.Center.Ground(ground));
                multimesh.SetInstanceTransform(i, transform);
                multimesh.SetInstanceCustomData(i, new Color(1f, 0f, 0f, 0f));
                _bushes.Add(new Bush
                {
                    Id = prop.Id, Multimesh = multimesh, Index = i, Transform = transform,
                    Center = prop.Footprint.Center, Radius = radius, Bottom = ground, Top = prop.Top,
                });
            }
            // Two views of the same instances: the visible one never casts shadows (its dithering would speckle them), the
            // shadow one is drawn only into shadow maps, undithered, so a see-through bush still casts a solid shadow.
            AddChild(new MultiMeshInstance3D
            {
                Multimesh = multimesh,
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                ExtraCullMargin = 1.5f,   // wind and push move leaves a little outside the instance bounds
            });
            AddChild(new MultiMeshInstance3D
            {
                Multimesh = multimesh,
                MaterialOverride = shadowMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly,
                ExtraCullMargin = 1.5f,
            });
        }
    }

    /// <param name="camera">Camera position this frame.</param>
    /// <param name="followed">The followed tank's hull footprint; null for a free camera, where only the camera clears bushes.</param>
    public void Update(Vector3 camera, Obb2? followed, float delta)
    {
        Obb2 hull = followed ?? default;
        bool hasHull = followed is not null;
        Obb2 own = hull with { HalfExtents = hull.HalfExtents + new SN.Vector2(OwnTankMargin) };
        Obb2 pushing = hull with { HalfExtents = hull.HalfExtents + new SN.Vector2(PushMargin) };
        var eye = new SN.Vector2(camera.X, camera.Z);
        foreach (Bush bush in _bushes)
        {
            if (!bush.Shown) continue;   // shot away: nothing to fade or push, nothing to upload
            bool clear = DistanceToBush(camera, bush) < NearCamera
                || (hasHull && (own.IntersectsCircle(bush.Center, bush.Radius) || SegmentDistance(bush.Center, eye, hull.Center) <= bush.Radius));
            float target = clear ? SeeThrough : 1f;
            bush.Opacity = Mathf.MoveToward(bush.Opacity, target, (1f - SeeThrough) * delta / FadeSeconds);

            if (hasHull && pushing.IntersectsCircle(bush.Center, bush.Radius))
            {
                bush.PushFrom = hull.Center;
                bush.Push = Mathf.MoveToward(bush.Push, 1f, delta / GrowSeconds);
            }
            else bush.Push = Mathf.MoveToward(bush.Push, 0f, delta / ReboundSeconds);

            var data = new Color(bush.Opacity, Ease(bush.Push), bush.PushFrom.X, bush.PushFrom.Y);
            if (data == bush.Uploaded) continue;
            bush.Multimesh.SetInstanceCustomData(bush.Index, data);
            bush.Uploaded = data;
        }
    }

    /// <summary>Hides the bushes that have been shot away (scaled to nothing) and shows restored ones again.</summary>
    public void SyncStanding(Battlefield field)
    {
        foreach (Bush bush in _bushes)
        {
            bool standing = field.IsStanding(bush.Id);
            if (standing == bush.Shown) continue;
            bush.Shown = standing;
            bush.Multimesh.SetInstanceTransform(bush.Index, standing ? bush.Transform : bush.Transform.ScaledLocal(Vector3.Zero));
        }
    }

    /// <summary>Distance from <paramref name="p"/> to the segment a→b.</summary>
    private static float SegmentDistance(SN.Vector2 p, SN.Vector2 a, SN.Vector2 b)
    {
        SN.Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared < 1e-6f ? 0f : Math.Clamp(SN.Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
        return SN.Vector2.Distance(p, a + ab * t);
    }

    /// <summary>Smoothstep, so the spring-back eases out instead of stopping dead.</summary>
    private static float Ease(float t) => t * t * (3f - 2f * t);

    /// <summary>Distance from the eye to the bush's upright cylinder (0 inside it).</summary>
    private static float DistanceToBush(Vector3 eye, Bush bush)
    {
        float horizontal = MathF.Max(new Vector2(eye.X - bush.Center.X, eye.Z - bush.Center.Y).Length() - bush.Radius, 0f);
        float vertical = eye.Y > bush.Top ? eye.Y - bush.Top : eye.Y < bush.Bottom ? bush.Bottom - eye.Y : 0f;
        return MathF.Sqrt(horizontal * horizontal + vertical * vertical);
    }
}
