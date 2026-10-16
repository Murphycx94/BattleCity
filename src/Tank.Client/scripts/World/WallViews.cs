using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim;
using Tank.Sim.Maps;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>
/// Draws the walls and fences (stage 2C design §4.1) and keeps them in step with the battlefield every frame
/// (<see cref="Sync"/>): a wall that is down shows a heap of rubble instead of its block; a fence knocked down topples
/// away from what hit it over <see cref="FenceFall.Seconds"/> (the side comes from its <see cref="GameEventType.PropDestroyed"/>
/// event, <see cref="OnEvent"/>) and lies flat on its posts' face; a restored prop stands up again at once. Meshes are
/// shared per kind (four rubble heaps per wall kind, picked by prop id); all of them get the rocks' soft sky rim light.
/// </summary>
public partial class WallViews : Node3D
{
    private const int RubbleVariants = 4;
    private const float RubbleHeight = 0.8f;
    private const float RubbleSink = 0.1f;    // the heap sits this far into the ground, so it doesn't float on a slope
    private const float RimStrength = 0.25f;  // the same rim light as the rocks (RockMeshes)
    private static readonly Vector2 BrickSize = new(0.5f, 0.25f), StoneSize = new(0.95f, 0.6f);
    private const float BrickJoint = 0.022f, StoneJoint = 0.035f;

    // A fallen panel turns about, and rests on, its posts' face: they are the fence's thickest part.
    private static readonly float FencePost = MathF.Min(WallShapes.PostWidth, WallLine.Thickness(PropKind.Fence));

    private readonly List<View> _views = new();
    private readonly Dictionary<int, View> _fences = new();   // prop id → its view, for the pushes in OnEvent

    private sealed class View
    {
        public required int Id { get; init; }
        public required Node3D Standing { get; init; }
        public required Transform3D Upright { get; init; }
        public MeshInstance3D? Rubble { get; init; }
        public bool ShownStanding = true;
        public float Fallen = -1f;   // seconds since a fence started toppling; -1 standing
        public int Side = 1;
        public SN.Vector2? Push;     // the level direction a fence was last pushed down, if any
    }

    public void Build(Battlefield field)
    {
        var brick = Jointed(Palette.Brick, Palette.BrickMortar, mode: 0, BrickSize, BrickJoint);
        var stone = Jointed(Palette.Stone, Palette.StoneMortar, mode: 1, StoneSize, StoneJoint);
        var brickRubble = Plain(Palette.Brick);
        var stoneRubble = Plain(Palette.Stone);
        var wood = Plain(Palette.Wood);
        var blocks = new Dictionary<PropKind, ArrayMesh>();
        var heaps = new Dictionary<(PropKind, int), ArrayMesh>();
        foreach (Prop prop in field.Props)
        {
            if (!prop.IsWall && prop.Kind != PropKind.Fence) continue;
            float thickness = WallLine.Thickness(prop.Kind);
            if (!blocks.TryGetValue(prop.Kind, out ArrayMesh? mesh))
                blocks[prop.Kind] = mesh = (prop.Kind == PropKind.Fence
                    ? WallShapes.Fence(WallLine.SegmentLength, WallLine.Height(prop.Kind), thickness, WallLine.Sink)
                    : WallShapes.Block(WallLine.SegmentLength, WallLine.Height(prop.Kind), thickness, WallLine.Sink)).ToArrayMesh();
            var upright = new Transform3D(new Basis(Vector3.Up, prop.Footprint.Yaw), prop.Footprint.Center.Ground(prop.Bottom + WallLine.Sink));
            var standing = new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = prop.Kind switch { PropKind.BrickWall => brick, PropKind.StoneWall => stone, _ => wood },
                Transform = upright,
            };
            AddChild(standing);
            MeshInstance3D? rubble = null;
            if (prop.IsWall)
            {
                int variant = prop.Id % RubbleVariants;
                if (!heaps.TryGetValue((prop.Kind, variant), out ArrayMesh? heap))
                    heaps[(prop.Kind, variant)] = heap = WallShapes.Rubble(WallLine.SegmentLength, thickness + 2 * Prop.RubbleSpread,
                        RubbleHeight, blocky: prop.Kind == PropKind.BrickWall, seed: 977u * (uint)(variant + 1) + (uint)prop.Kind).ToArrayMesh();
                rubble = new MeshInstance3D
                {
                    Mesh = heap,
                    MaterialOverride = prop.Kind == PropKind.BrickWall ? brickRubble : stoneRubble,
                    Transform = upright.Translated(Vector3.Down * RubbleSink),
                    Visible = false,
                };
                AddChild(rubble);
            }
            var view = new View { Id = prop.Id, Standing = standing, Upright = upright, Rubble = rubble };
            _views.Add(view);
            if (prop.Kind == PropKind.Fence) _fences[prop.Id] = view;
        }
    }

    /// <summary>Notes which way a fence was pushed, so it falls that way when <see cref="Sync"/> sees it down.</summary>
    public void OnEvent(in GameEvent e)
    {
        if (e.Type == GameEventType.PropDestroyed && _fences.TryGetValue(e.PropId, out View? fence))
            fence.Push = new SN.Vector2(e.Velocity.X, e.Velocity.Z);
    }

    /// <summary>Shows each wall and fence as the battlefield has it now, and moves toppling fences on.</summary>
    public void Sync(Battlefield field, float delta)
    {
        foreach (View view in _views)
        {
            bool standing = field.IsStanding(view.Id);
            if (standing != view.ShownStanding) Change(view, standing);
            if (view.Fallen < 0f || view.Fallen > FenceFall.Seconds) continue;
            view.Fallen += delta;
            float progress = FenceFall.Progress(view.Fallen);
            view.Standing.Transform = view.Upright * FenceFall.Pose(progress, view.Side, FencePost).ToGodot();
        }
    }

    private static void Change(View view, bool standing)
    {
        view.ShownStanding = standing;
        if (view.Rubble is { } rubble)
        {
            rubble.Visible = !standing;
            view.Standing.Visible = standing;
            return;
        }
        if (standing)
        {
            view.Fallen = -1f;
            view.Standing.Transform = view.Upright;
            return;
        }
        // Falls away from the push: towards the panel's +Z face if the push points that way. With no push on record it
        // falls towards its −Z face, the footprint's forward.
        Vector3 across = view.Upright.Basis.Z;
        view.Side = view.Push is { } push && push.X * across.X + push.Y * across.Z >= 0 ? 1 : -1;
        view.Fallen = 0f;
    }

    /// <summary>Plain colour times the mesh's vertex shade (wall.gdshader mode 2): rubble chunks and fence wood.</summary>
    private static ShaderMaterial Plain(SN.Vector3 color) => Wall(color, mode: 2);

    /// <summary>A standing wall with joint lines drawn on it: staggered bricks (mode 0) or uneven stone blocks (mode 1).</summary>
    private static ShaderMaterial Jointed(SN.Vector3 color, SN.Vector3 joint, int mode, Vector2 block, float jointWidth) =>
        Wall(color, mode)
            .With("joint_color", joint.ToColor())
            .With("block", block)
            .With("joint", jointWidth);

    /// <summary>A wall.gdshader material of one colour, with the rocks' rim light.</summary>
    private static ShaderMaterial Wall(SN.Vector3 color, int mode) =>
        Materials.FromShader("wall")
            .With("base_color", color.ToColor())
            .With("mode", mode)
            .With("toon_rim_strength", RimStrength);
}
