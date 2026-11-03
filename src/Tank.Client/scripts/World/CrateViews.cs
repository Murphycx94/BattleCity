using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Client.Combat;
using Tank.Net.Battle;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Terrain;

namespace Tank.Client.World;

/// <summary>
/// The supply crates in the match scene (stage 6 design §4.3), drawn from what the client knows
/// (<see cref="CrateKnowledge"/>: a crate once seen stays until its spot is seen empty or the player eats it). One node
/// per known, standing crate: the box (<see cref="CrateMesh.Body"/>) on the ground — as low as its lowest corner, so it
/// never floats on a slope — turned by its id (<see cref="CrateMesh.YawPerId"/>) so a row of crates doesn't line up,
/// with the sign and its post as a child the view bobs up and down every frame (<see cref="CrateMesh.BobPeriod"/>,
/// offset by the id so they don't bob in step). The meshes (one box, one sign a kind) and the material are built once
/// (<see cref="Build"/>); the 64 entries are scanned again only when the knowledge's version changes. A crate no longer
/// standing (or no longer known) goes; it goes with a pickup flash in its kind's colour (<see cref="BattleFx.Pickup"/>,
/// <see cref="FlashHeight"/> up) only when the player saw it eaten — it was in sight the frame before, or the scene
/// passed on the player's own pickup (<see cref="NoteOwnPickup"/>) — and silently otherwise: a spot re-seen empty, a
/// crate a resync no longer knows, the lot cleared on leaving (<see cref="CrateFlash.FlashesOnRemove"/>, plan deviation
/// 40). Nothing managed is allocated per frame.
/// </summary>
public partial class CrateViews : Node3D
{
    /// <summary>m above the ground: where the pickup flash is centred, at the sign — above the hull in the chase camera.</summary>
    public const float FlashHeight = 1.6f;

    private const float RimStrength = 0.25f;   // the same sky rim light as the houses

    private readonly MeshInstance3D?[] _boxes = new MeshInstance3D?[CrateRules.MaxCrates];
    private readonly MeshInstance3D?[] _signs = new MeshInstance3D?[CrateRules.MaxCrates];
    private readonly CrateKind[] _kinds = new CrateKind[CrateRules.MaxCrates];   // the kind each node was built for: its flash's colour
    private readonly bool[] _wasVisible = new bool[CrateRules.MaxCrates];        // in sight at the end of the last frame
    private readonly bool[] _ownPickup = new bool[CrateRules.MaxCrates];         // the player ate it this frame
    private bool _anyOwnPickup;
    private readonly ArrayMesh[] _signMeshes = new ArrayMesh[CrateRules.Kinds];
    private ArrayMesh _boxMesh = null!;
    private ShaderMaterial _material = null!;
    private HeightField _terrain = null!;
    private Aabb _boxBounds, _signBounds;
    private int _version = -1;

    /// <summary>The meshes, the material and the terrain the crates stand on; no crates yet.</summary>
    public void Build(Battlefield field)
    {
        _terrain = field.Terrain;
        _material = Materials.FromShader("house").With("toon_rim_strength", RimStrength);
        MeshData body = CrateMesh.Body();
        _boxMesh = body.ToArrayMesh();
        _boxBounds = BoundsOf(body);
        for (int k = 0; k < CrateRules.Kinds; k++)
        {
            MeshData sign = CrateMesh.Build((CrateKind)k).Sign;
            _signMeshes[k] = sign.ToArrayMesh();
            if (k == 0) _signBounds = BoundsOf(sign);   // the signs differ only in their icons, well inside the plate
        }
    }

    /// <summary>
    /// The player's own pickup message named crate <paramref name="id"/> this frame: if the next <see cref="Sync"/> sees it
    /// gone, it flashes even if it was never in sight. Call before that frame's <see cref="Sync"/>, which forgets it.
    /// </summary>
    public void NoteOwnPickup(byte id)
    {
        if (id >= _ownPickup.Length) return;   // the knowledge refused it already
        _ownPickup[id] = true;
        _anyOwnPickup = true;
    }

    /// <summary>
    /// Once a frame: the nodes brought in step with <paramref name="crates"/> when its version changed (new crates stand up,
    /// gone ones go, with a flash through <paramref name="fx"/> if the player saw them eaten), and every sign bobbed for
    /// <paramref name="time"/> (the scene clock, s). Then what is in sight now is kept for the next frame's verdict.
    /// </summary>
    public void Sync(CrateKnowledge crates, double time, BattleFx fx)
    {
        ReadOnlySpan<CrateKnowledge.Entry> all = crates.All;
        if (crates.Version != _version)
        {
            _version = crates.Version;
            Rescan(all, fx);
        }
        for (int id = 0; id < all.Length; id++) _wasVisible[id] = all[id].Visible;   // Visible moves no version: read it every frame
        if (_anyOwnPickup)
        {
            Array.Clear(_ownPickup);
            _anyOwnPickup = false;
        }
        double turns = time / CrateMesh.BobPeriod;
        float phase = (float)(Math.Tau * (turns - Math.Floor(turns)));   // the whole turns dropped first: a long match keeps its precision
        for (int id = 0; id < _signs.Length; id++)
        {
            MeshInstance3D? sign = _signs[id];
            if (sign is null) continue;
            sign.Position = new Vector3(0f, CrateMesh.SignCentreHeight + CrateMesh.BobAmplitude * MathF.Sin(phase + id), 0f);
        }
    }

    private void Rescan(ReadOnlySpan<CrateKnowledge.Entry> all, BattleFx fx)
    {
        for (int id = 0; id < all.Length; id++)
        {
            ref readonly CrateKnowledge.Entry entry = ref all[id];
            bool standing = entry.Known && entry.Standing;
            bool drawn = _boxes[id] is not null;
            if (standing && !drawn) Add(id, entry);
            else if (!standing && drawn) Remove(id, CrateFlash.FlashesOnRemove(_wasVisible[id], _ownPickup[id], entry) ? fx : null);
        }
    }

    private void Add(int id, in CrateKnowledge.Entry entry)
    {
        float x = entry.Position.X, z = entry.Position.Y, yaw = id * CrateMesh.YawPerId;
        var box = new MeshInstance3D
        {
            Mesh = _boxMesh,
            MaterialOverride = _material,
            CustomAabb = _boxBounds,
            Transform = new Transform3D(new Basis(Vector3.Up, yaw), new Vector3(x, LowestCorner(x, z, yaw), z)),
        };
        var sign = new MeshInstance3D
        {
            Mesh = _signMeshes[(int)entry.Kind],
            MaterialOverride = _material,
            CustomAabb = _signBounds,
            Position = new Vector3(0f, CrateMesh.SignCentreHeight, 0f),
        };
        box.AddChild(sign);
        AddChild(box);
        _boxes[id] = box;
        _signs[id] = sign;
        _kinds[id] = entry.Kind;
    }

    /// <summary>The ground under the box turned by <paramref name="yaw"/> at (<paramref name="x"/>, <paramref name="z"/>): the lowest of its four corners, so the downhill one doesn't float.</summary>
    private float LowestCorner(float x, float z, float yaw)
    {
        (float sin, float cos) = MathF.SinCos(yaw);
        float h = CrateRules.HalfSize, lowest = float.MaxValue;
        for (int corner = 0; corner < 4; corner++)
        {
            float lx = (corner & 1) == 0 ? -h : h, lz = (corner & 2) == 0 ? -h : h;
            // Turned about y as the node's basis turns it: local +Z goes to (sin, cos).
            lowest = MathF.Min(lowest, _terrain.HeightAt(x + lx * cos + lz * sin, z - lx * sin + lz * cos));
        }
        return lowest;
    }

    /// <summary>The crate is gone: its nodes freed, with a flash in its colour where it stood if <paramref name="fx"/> is given.</summary>
    private void Remove(int id, BattleFx? fx)
    {
        MeshInstance3D box = _boxes[id]!;
        fx?.Pickup(box.GlobalPosition + Vector3.Up * FlashHeight, Palette.Crate(_kinds[id]).ToColor());
        box.QueueFree();
        _boxes[id] = null;
        _signs[id] = null;
    }

    /// <summary>The mesh's box in its own space. The sign's is its node's own, and the node carries it as it bobs: no room for the bob is needed.</summary>
    private static Aabb BoundsOf(MeshData mesh)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (System.Numerics.Vector3 p in mesh.Positions)
        {
            min = min.Min(new Vector3(p.X, p.Y, p.Z));
            max = max.Max(new Vector3(p.X, p.Y, p.Z));
        }
        return new Aabb(min, max - min);
    }
}
