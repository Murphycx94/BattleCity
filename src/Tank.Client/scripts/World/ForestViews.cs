using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>
/// Draws the map's tree props (riverland design §6.4), each the size of its prop (<see cref="TreeFit"/>): conifer or
/// broadleaf by <see cref="TreeFit.IsConifer"/> under the map's forests, two meshes of each picked by id. The trees are grouped into
/// <see cref="SectorSize"/> m sectors by their trunk's centre (<see cref="SectorGrid"/>), with one MultiMesh per sector and
/// mesh, so frustum culling drops whole sectors and each fades out beyond <see cref="VisibleRange"/> like the tree line. A
/// shadow-only view of the same instances, undithered, casts their shadows within <see cref="ShadowRange"/>.
/// <see cref="Sync"/> keeps them in step with the battlefield and the camera:
/// - a tree felled falls about its root the way it was pushed (<see cref="OnEvent"/>, <see cref="TreeFall"/>) and lies
///   there; one whose push comes after it started down (a match, <see cref="PropDown"/>) turns that way, at once if it
///   already lies; a restored tree stands up again at once; the first look after <see cref="Build"/> lays a tree that is
///   already down straight at rest;
/// - trees within <see cref="SeeThroughFades.NearCamera"/> of the camera, and trees whose crown overlaps the ground line
///   from the camera to the player's own tank, fade (<see cref="SeeThroughFades"/>; screen-door dithering in
///   tree.gdshader, as the bushes) and back once clear. A falling or fallen tree's crown is looked for along the way it
///   fell (<see cref="CrownAlong"/>), so one going over in front of the tank clears too.
/// Per frame only the falling trees, the fading ones, the trees that are down and the standing ones in the sectors around
/// the camera and along that line are looked at.
/// </summary>
public partial class ForestViews : Node3D
{
    public const float SectorSize = 80f;
    public const float VisibleRange = 520f;
    private const float VisibleFade = 60f;
    public const float ShadowRange = 150f;
    private const float Sink = 0.1f;          // roots a little below the ground on uneven terrain, as the tree line
    // Where a leaning tree's crown is looked for: these fractions of its height along the way it fell (its trunk's centre
    // while it stands). The trees that are down are all looked at, wherever their sector.
    private static readonly float[] CrownAlong = { 0.35f, 0.65f, 0.9f };
    private const uint Seed = 5323u;

    private readonly SectorGrid _grid = new(SectorSize);
    private readonly Dictionary<(int X, int Z), int[]> _sectors = new();   // sector → indices into _trees
    private readonly Dictionary<int, int> _byId = new();                   // prop id → index into _trees
    private readonly List<MultiMeshInstance3D> _visible = new();
    private readonly List<int> _falling = new();
    private readonly List<int> _down = new();      // every tree not standing: its crown may lie well outside its sector
    private readonly List<int> _faded = new();     // the trees whose opacity changed this frame
    private TreeView[] _trees = Array.Empty<TreeView>();
    private SeeThroughFades _fades = new(0);
    private float _reach;              // the widest crown: how far a standing tree reaches out of its sector
    private Battlefield? _lastField;   // only to notice a resync's new instance; Sync is always given the current one
    private int _version = -1;

    private struct TreeView
    {
        public int Id;
        public MultiMesh Multimesh;
        public int Index;
        public SN.Matrix4x4 Upright;   // its yaw and scale, then its root's place (row vectors)
        public Color Data;             // r, g, b as the tree line's; a is written from its opacity
        public SN.Vector2 Centre;
        public float Crown;
        public float Bottom;
        public float Top;
        public float Height;           // its own height, ground to top
        public PropDown Down;
        public SN.Vector3 Axis;        // what it turns about while down
        public SN.Vector2 Along;       // the level direction it falls, unit
        public float Lean;             // sin of how far over it is: 0 standing, about 1 lying
    }

    /// <param name="forests">The map's forests, by which its trees are conifers or broadleaves (<see cref="TreeFit.ForestsOf"/>).</param>
    public void Build(Battlefield field, IReadOnlyList<TreeFit.ConiferRegion> forests)
    {
        var meshes = new Mesh[2 * TreeFit.Variants];
        var shapes = new TreeShapes.Shape[2 * TreeFit.Variants];
        for (int m = 0; m < meshes.Length; m++)
        {
            var (mesh, shape) = TreeFit.Variant(conifer: m < TreeFit.Variants, m % TreeFit.Variants);
            (meshes[m], shapes[m]) = (mesh.ToArrayMesh(), shape);
        }
        ShaderMaterial material = Materials.FromShader("tree");
        ShaderMaterial shadowMaterial = Materials.FromShader("tree").With("dither", false);
        IReadOnlyList<Prop> props = field.Props;
        Dictionary<(int X, int Z), List<int>> sectors = SectorGrid.GroupByCentre(props, p => p.Kind == PropKind.Tree, SectorSize);
        var trees = new List<TreeView>();
        var bySlot = new List<int>[meshes.Length];
        for (int m = 0; m < bySlot.Length; m++) bySlot[m] = new List<int>();
        foreach (var (key, indices) in sectors.OrderBy(entry => entry.Key))
        {
            foreach (List<int> slot in bySlot) slot.Clear();
            foreach (int i in indices)
                bySlot[(TreeFit.IsConifer(props[i], forests) ? 0 : TreeFit.Variants) + props[i].Id % TreeFit.Variants].Add(i);
            var inSector = new List<int>(indices.Count);
            for (int m = 0; m < meshes.Length; m++)
            {
                if (bySlot[m].Count == 0) continue;
                var multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseCustomData = true,
                    Mesh = meshes[m],
                    InstanceCount = bySlot[m].Count,
                };
                for (int k = 0; k < bySlot[m].Count; k++)
                {
                    TreeView tree = Instance(props[bySlot[m][k]], field, shapes[m], multimesh, k);
                    multimesh.SetInstanceTransform(k, tree.Upright.ToGodot());
                    multimesh.SetInstanceCustomData(k, tree.Data);
                    _byId[tree.Id] = trees.Count;
                    inSector.Add(trees.Count);
                    trees.Add(tree);
                    _reach = MathF.Max(_reach, tree.Crown);
                }
                AddViews(multimesh, material, shadowMaterial);
            }
            _sectors[key] = inSector.ToArray();
        }
        _trees = trees.ToArray();
        _fades = new SeeThroughFades(_trees.Length);
        _falling.Capacity = _trees.Length;
        _down.Capacity = _trees.Length;
    }

    /// <summary>
    /// With <paramref name="far"/> every sector is drawn at any distance (the top-down map view); else each fades out
    /// beyond <see cref="VisibleRange"/>, as built. Shadows keep their range.
    /// </summary>
    public void SetFarView(bool far)
    {
        foreach (MultiMeshInstance3D view in _visible) view.VisibilityRangeEnd = far ? 0f : VisibleRange;   // 0: no end
    }

    /// <summary>
    /// Notes which way a tree was pushed, so it falls that way when <see cref="Sync"/> sees it down; if it is down already
    /// (a match), turns it that way now: the rest of its fall goes there, or one lying is laid there at once.
    /// </summary>
    public void OnEvent(in GameEvent e)
    {
        if (e.Type != GameEventType.PropDestroyed || !_byId.TryGetValue(e.PropId, out int index)) return;
        ref TreeView tree = ref _trees[index];
        if (!tree.Down.Pushed(new SN.Vector2(e.Velocity.X, e.Velocity.Z))) return;
        Aim(ref tree);
        if (tree.Down.AtRest) Lay(tree);
    }

    /// <param name="field">The battlefield now (a resync swaps the instance): its trees' standing is read again whenever it
    /// or its <see cref="Battlefield.StateVersion"/> changes.</param>
    /// <param name="cameraPos">Camera position this frame.</param>
    /// <param name="ownHull">The player's own tank's hull; null for a free camera, where only the camera clears trees.</param>
    public void Sync(Battlefield field, Vector3 cameraPos, Obb2? ownHull, float delta)
    {
        if (!ReferenceEquals(field, _lastField) || field.StateVersion != _version)
        {
            bool first = _lastField is null;
            (_lastField, _version) = (field, field.StateVersion);
            SyncStanding(field, first);
        }
        Fall(delta);
        SeeThroughTrees(cameraPos, ownHull, delta);
    }

    private TreeView Instance(in Prop prop, Battlefield field, TreeShapes.Shape shape, MultiMesh multimesh, int index)
    {
        SN.Vector2 centre = prop.Footprint.Center;
        float ground = TerrainMesh.VisualHeightAt(field.Terrain, centre.X, centre.Y);
        float yaw = SeededNoise.Hash01(prop.Id, 1, 0, Seed) * Mathf.Tau;
        SN.Matrix4x4 upright = SN.Matrix4x4.CreateScale(TreeFit.Scale(prop, shape)) * SN.Matrix4x4.CreateRotationY(yaw)
            * SN.Matrix4x4.CreateTranslation(centre.X, ground - Sink, centre.Y);
        return new TreeView
        {
            Id = prop.Id, Multimesh = multimesh, Index = index, Upright = upright,
            Data = new Color(SeededNoise.Hash01(prop.Id, 2, 0, Seed), SeededNoise.Hash01(prop.Id, 3, 0, Seed), SeededNoise.Hash01(prop.Id, 4, 0, Seed), 0f),
            Centre = centre, Crown = prop.FoliageRadius, Bottom = ground, Top = prop.Top, Height = prop.HeightAboveGround(),
            Down = new PropDown(TreeFall.Seconds),
        };
    }

    /// <summary>The visible view of a sector's mesh (fading out with distance, never casting shadows) and its shadow-only view.</summary>
    private void AddViews(MultiMesh multimesh, Material material, Material shadowMaterial)
    {
        var visible = new MultiMeshInstance3D
        {
            Multimesh = multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,   // its dithering would speckle the shadow
            VisibilityRangeEnd = VisibleRange,
            VisibilityRangeEndMargin = VisibleFade,
            VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
        };
        AddChild(visible);
        _visible.Add(visible);
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = multimesh,
            MaterialOverride = shadowMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly,
            VisibilityRangeEnd = ShadowRange,
        });
    }

    /// <summary>
    /// Starts the trees felled since the last look falling (on the <paramref name="first"/> look, lays them down at rest),
    /// stands restored ones up again at once, and lists the trees that are down.
    /// </summary>
    private void SyncStanding(Battlefield field, bool first)
    {
        _down.Clear();
        for (int i = 0; i < _trees.Length; i++)
        {
            ref TreeView tree = ref _trees[i];
            switch (tree.Down.See(field.IsStanding(tree.Id), snap: first))
            {
                case PropChange.Rose:
                    tree.Lean = 0f;
                    tree.Multimesh.SetInstanceTransform(tree.Index, tree.Upright.ToGodot());
                    break;
                case PropChange.Fell:
                    Aim(ref tree);
                    if (tree.Down.Moving) _falling.Add(i);
                    else Lay(tree);
                    break;
            }
            if (!tree.Down.Standing) _down.Add(i);
        }
    }

    /// <summary>Sets the way a tree going down falls: the way it was pushed, else one from its id; and how far over it is.</summary>
    private static void Aim(ref TreeView tree)
    {
        tree.Axis = TreeFall.Axis(tree.Down.Push ?? SN.Vector2.Zero, tree.Id);
        tree.Along = TreeFall.Along(tree.Axis);
        tree.Lean = MathF.Sin(TreeFall.Angle(tree.Down.Elapsed));
    }

    /// <summary>Draws a tree that is down as far over as it is now.</summary>
    private static void Lay(in TreeView tree) =>
        tree.Multimesh.SetInstanceTransform(tree.Index, TreeFall.Pose(tree.Upright, tree.Axis, tree.Down.Elapsed).ToGodot());

    /// <summary>Moves the falling trees on; one that has come to rest (or was stood up again) drops off the list.</summary>
    private void Fall(float delta)
    {
        for (int k = _falling.Count - 1; k >= 0; k--)
        {
            ref TreeView tree = ref _trees[_falling[k]];
            if (tree.Down.Standing)
            {
                _falling.RemoveSwapAt(k);
                continue;
            }
            bool rested = tree.Down.Step(delta);
            tree.Lean = MathF.Sin(TreeFall.Angle(tree.Down.Elapsed));
            Lay(tree);
            if (rested) _falling.RemoveSwapAt(k);
        }
    }

    /// <summary>
    /// Marks the trees in the way this frame (the standing ones in the sectors near the camera and along the line to the
    /// tank, or all of them when those sectors are too many, and every one that is down), then fades every tree in the way or still fading, uploading what changed.
    /// </summary>
    private void SeeThroughTrees(Vector3 camera, Obb2? ownHull, float delta)
    {
        _fades.BeginFrame();
        var eye = new SN.Vector2(camera.X, camera.Z);
        Span<(int X, int Z)> keys = stackalloc (int X, int Z)[SeeThroughFades.MaxSectors];
        int count = _grid.InView(eye, ownHull?.Center, SeeThroughFades.NearCamera, _reach, keys);
        if (count < 0)   // more sectors than keys: look at every tree
        {
            for (int i = 0; i < _trees.Length; i++)
                if (_trees[i].Down.Standing && InWay(_trees[i], camera, eye, ownHull)) _fades.MarkInWay(i);
        }
        for (int k = 0; k < count; k++)
        {
            if (!_sectors.TryGetValue(keys[k], out int[]? indices)) continue;
            foreach (int i in indices)
                if (_trees[i].Down.Standing && InWay(_trees[i], camera, eye, ownHull)) _fades.MarkInWay(i);
        }
        foreach (int i in _down)
            if (InWay(_trees[i], camera, eye, ownHull)) _fades.MarkInWay(i);
        _fades.Step(delta, _faded);
        foreach (int i in _faded)
        {
            ref TreeView tree = ref _trees[i];
            tree.Multimesh.SetInstanceCustomData(tree.Index, tree.Data with { A = 1f - _fades.OpacityOf(i) });
        }
    }

    /// <summary>
    /// Whether <paramref name="tree"/>'s crown is within <see cref="SeeThroughFades.NearCamera"/> of the camera or over the ground line from
    /// it to <paramref name="ownHull"/>'s centre: a standing tree's crown round its trunk, a leaning one's at each of
    /// <see cref="CrownAlong"/> (its height range left as standing: a little generous).
    /// </summary>
    private static bool InWay(in TreeView tree, Vector3 camera, SN.Vector2 eye, Obb2? ownHull)
    {
        if (tree.Lean == 0f) return CrownInWay(tree, tree.Centre, camera, eye, ownHull);
        foreach (float along in CrownAlong)
            if (CrownInWay(tree, tree.Centre + tree.Along * (tree.Lean * tree.Height * along), camera, eye, ownHull)) return true;
        return false;
    }

    private static bool CrownInWay(in TreeView tree, SN.Vector2 crown, Vector3 camera, SN.Vector2 eye, Obb2? ownHull) =>
        DistanceToCrown(camera, tree, crown) < SeeThroughFades.NearCamera || (ownHull is { } own && SegmentDistance(crown, eye, own.Center) <= tree.Crown);

    /// <summary>Distance from the eye to the upright cylinder of the tree's crown radius round <paramref name="crown"/>, ground to top (0 inside it).</summary>
    private static float DistanceToCrown(Vector3 eye, in TreeView tree, SN.Vector2 crown)
    {
        float horizontal = MathF.Max(new Vector2(eye.X - crown.X, eye.Z - crown.Y).Length() - tree.Crown, 0f);
        float vertical = eye.Y > tree.Top ? eye.Y - tree.Top : eye.Y < tree.Bottom ? tree.Bottom - eye.Y : 0f;
        return MathF.Sqrt(horizontal * horizontal + vertical * vertical);
    }

    /// <summary>Distance from <paramref name="p"/> to the segment a→b.</summary>
    private static float SegmentDistance(SN.Vector2 p, SN.Vector2 a, SN.Vector2 b)
    {
        SN.Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared < 1e-6f ? 0f : Math.Clamp(SN.Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
        return SN.Vector2.Distance(p, a + ab * t);
    }
}
