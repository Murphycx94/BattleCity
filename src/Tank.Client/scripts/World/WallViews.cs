using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim;
using Tank.Sim.Maps;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>
/// Draws the walls and fences (stage 2C design §4.1) and keeps them in step with the battlefield: <see cref="SyncStanding"/>
/// looks at every wall and fence (the caller does it only when the battlefield's state changed), <see cref="Fall"/> moves
/// the toppling fences on every frame. A wall that is down shows a heap of rubble instead of its block; a fence knocked
/// down topples away from what hit it over <see cref="FenceFall.Seconds"/> (the side comes from its
/// <see cref="GameEventType.PropDestroyed"/> event, <see cref="OnEvent"/>; one whose push comes after it started toppling,
/// in a match, turns to fall that way, <see cref="PropDown"/>) and lies flat on its posts' face; a restored prop stands up
/// again at once. The first look after <see cref="Build"/> shows a fence already down lying flat at once. Meshes are shared
/// per kind (four rubble heaps per wall kind, picked by prop id); all of them get the rocks' soft sky rim light.
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
    private readonly List<View> _falling = new();             // fences on their way down, moved on by Fall
    private bool _seen;   // SyncStanding has looked at the battlefield once

    private sealed class View
    {
        public required int Id { get; init; }
        public required Node3D Standing { get; init; }
        public required Transform3D Upright { get; init; }
        public MeshInstance3D? Rubble { get; init; }
        public PropDown Down = new(FenceFall.Seconds);   // a wall shows its rubble at once, whatever the duration
        public int Side = 1;
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
            // Wall segments and fence panels only: a house is masonry too (IsWall), but not a segment.
            if (prop.Kind is not (PropKind.BrickWall or PropKind.StoneWall or PropKind.Fence)) continue;
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
        _falling.EnsureCapacity(_fences.Count);   // room for every fence at once: the list never grows mid-match
    }

    /// <summary>
    /// Notes which way a fence was pushed, so it falls that way when <see cref="SyncStanding"/> sees it down; if it is down already
    /// (a match), turns it to fall that way now, or lays it there at once if it lies.
    /// </summary>
    public void OnEvent(in GameEvent e)
    {
        if (e.Type != GameEventType.PropDestroyed || !_fences.TryGetValue(e.PropId, out View? fence)) return;
        if (!fence.Down.Pushed(new SN.Vector2(e.Velocity.X, e.Velocity.Z))) return;
        fence.Side = SideOf(fence);
        if (fence.Down.AtRest) Pose(fence);
    }

    /// <summary>
    /// Shows each wall and fence as the battlefield has it now: rubble for a wall that is down, a fence knocked down starts
    /// toppling (<see cref="Fall"/> moves it on), a restored prop stands up again.
    /// </summary>
    public void SyncStanding(Battlefield field)
    {
        bool first = !_seen;
        _seen = true;
        foreach (View view in _views)
        {
            PropChange change = view.Down.See(field.IsStanding(view.Id), snap: first);
            if (change == PropChange.None) continue;
            Change(view, change);
            if (view.Rubble is null && view.Down.Moving) _falling.Add(view);
        }
    }

    /// <summary>Moves the toppling fences on, every frame; one that has come to rest (or was stood up again) drops off the list.</summary>
    public void Fall(float delta)
    {
        for (int k = _falling.Count - 1; k >= 0; k--)
        {
            View fence = _falling[k];
            if (fence.Down.Standing)
            {
                _falling.RemoveSwapAt(k);
                continue;
            }
            bool rested = fence.Down.Step(delta);
            Pose(fence);
            if (rested) _falling.RemoveSwapAt(k);
        }
    }

    private static void Change(View view, PropChange change)
    {
        bool standing = change == PropChange.Rose;
        if (view.Rubble is { } rubble)
        {
            rubble.Visible = !standing;
            view.Standing.Visible = standing;
            return;
        }
        if (standing)
        {
            view.Standing.Transform = view.Upright;
            return;
        }
        view.Side = SideOf(view);
        if (view.Down.AtRest) Pose(view);
    }

    /// <summary>
    /// The side a fence falls to, away from its push: towards the panel's +Z face if the push points that way. With no
    /// push on record it falls towards its −Z face, the footprint's forward.
    /// </summary>
    private static int SideOf(View fence)
    {
        Vector3 across = fence.Upright.Basis.Z;
        return fence.Down.Push is { } push && push.X * across.X + push.Y * across.Z >= 0 ? 1 : -1;
    }

    /// <summary>Draws a fence that is down as far over as it is now.</summary>
    private static void Pose(View fence) =>
        fence.Standing.Transform = fence.Upright * FenceFall.Pose(FenceFall.Progress(fence.Down.Elapsed), fence.Side, FencePost).ToGodot();

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
