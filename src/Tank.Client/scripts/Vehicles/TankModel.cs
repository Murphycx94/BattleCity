using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Client.World;
using Tank.Sim.Vehicles;

namespace Tank.Client.Vehicles;

/// <summary>
/// Procedural cartoon tank built from its <see cref="TankLook"/>, painted with tank_toon.gdshader. Node layout keeps the stage-1 contract: the root sits at the
/// hull origin on the ground, "Turret" yaws, "Gun" pitches, the barrel points −Z. Below that:
/// Body (visual nod) → Hull; Turret (a sibling of Body, so nod never tilts the gun) → Toss (pivot in the dome, moved only
/// when the tank is wrecked) → TurretBody → TurretShell, Gun → Mantlet, Barrel (recoil: tube and bulb), Muzzle;
/// Running gear → Wheels, Tracks. A wreck chars black and throws its turret (<see cref="TurretToss"/>) until <see cref="Restore"/>.
/// </summary>
public partial class TankModel : Node3D
{
    private const float RimStrength = 0.25f;
    private const int PaintMode = 0, WheelMode = 1, TrackMode = 2;   // tank_toon.gdshader "mode"
    private const float CharSeconds = 0.6f;

    private readonly Node3D _body = new() { Name = "Body" };
    private readonly Node3D _turret = new() { Name = "Turret" };
    private readonly Node3D _toss = new() { Name = "Toss" };
    private readonly Node3D _turretBody = new() { Name = "TurretBody" };
    private readonly Node3D _gun = new() { Name = "Gun" };
    private readonly Node3D _barrel = new() { Name = "Barrel" };
    private readonly Marker3D _muzzle = new() { Name = "Muzzle" };
    private readonly Node3D _wheels = new() { Name = "Wheels" };
    private readonly MultiMeshInstance3D _tracks = new() { Name = "Tracks" };
    private readonly List<WheelNode> _wheelNodes = new();
    private readonly List<ShaderMaterial> _materials = new();
    private readonly record struct WheelNode(Node3D Node, float Radius, bool Left, float Phase);
    private MultiMesh _linkMesh = null!;
    private float[] _linkBuffer = Array.Empty<float>();
    private float _placedLeft = float.NaN;
    private float _placedRight = float.NaN;
    private TankLook _look = null!;
    private BeltPath _belt = null!;
    private int _linksPerSide;
    private Vector3 _nodPivot;
    private Vector3 _turretBase;
    private TankPaint _paint;
    private TurretToss _tossPath = null!;
    private float _wreckTime = -1f;   // seconds since the kill; negative while intact
    private float _tossYaw;
    private float _charShown;

    public TankAnimator Animator { get; private set; } = null!;

    /// <summary>Barrel tip in world space, for muzzle effects.</summary>
    public Transform3D MuzzleTransform => _muzzle.GlobalTransform;

    /// <param name="paint">Overrides the look's paint (the practice targets are blue-grey).</param>
    public void Build(TankSpec spec, TankPaint? paint = null)
    {
        _look = TankLooks.For(spec.Id);
        _paint = paint ?? _look.Paint;
        Animator = new TankAnimator(_look.TrackX);
        _tossPath = new TurretToss(_look, spec.TurretOffset);
        _nodPivot = new Vector3(0, _look.RoadWheelY, 0);
        ShaderMaterial paintMaterial = ToonMaterial(PaintMode);
        // Same paint on the gun; its origin is the trunnion, so lift it by that height for the bottom-to-top shading.
        ShaderMaterial gunPaint = ToonMaterial(PaintMode).With("height_offset", spec.TrunnionHeight);

        AddChild(_body);
        _body.AddChild(new MeshInstance3D { Name = "Hull", Mesh = TankParts.Hull(_look).ToArrayMesh(), MaterialOverride = paintMaterial });

        _turretBase = new Vector3(0, 0, -spec.TurretOffset);
        _turret.Position = _turretBase;
        AddChild(_turret); // sibling of Body: the hull nods under it, but the gun line stays exactly the sim's
        // The toss pivot sits in the dome; TurretBody undoes its height, so intact the turret is exactly where it was.
        float pivotHeight = _tossPath.Pivot.Y;
        _toss.Position = new Vector3(0, pivotHeight, 0);
        _turret.AddChild(_toss);
        _turretBody.Position = new Vector3(0, -pivotHeight, 0);
        _toss.AddChild(_turretBody);
        _turretBody.AddChild(new MeshInstance3D { Name = "TurretShell", Mesh = TankParts.Turret(_look).ToArrayMesh(), MaterialOverride = paintMaterial });

        _gun.Position = new Vector3(0, spec.TrunnionHeight, _look.GunPivotZ);
        _turretBody.AddChild(_gun);
        _gun.AddChild(new MeshInstance3D { Name = "Mantlet", Mesh = TankParts.Mantlet(_look).ToArrayMesh(), MaterialOverride = gunPaint });
        _gun.AddChild(_barrel);
        float barrelLength = spec.GunLength + _look.GunPivotZ;
        _barrel.AddChild(new MeshInstance3D { Mesh = TankParts.Barrel(_look, barrelLength).ToArrayMesh(), MaterialOverride = gunPaint });
        _barrel.AddChild(new MeshInstance3D { Name = "MuzzleBulb", Mesh = TankParts.Muzzle(_look, barrelLength).ToArrayMesh(), MaterialOverride = ToonMaterial(TrackMode) });
        _muzzle.Position = new Vector3(0, 0, -barrelLength);
        _gun.AddChild(_muzzle);

        AddChild(_wheels);
        BuildWheels();
        AddChild(_tracks);
        BuildTracks(ToonMaterial(TrackMode));
    }

    public bool IsWrecked => _wreckTime >= 0f;

    /// <summary>Knocks the tank out: the turret flies off and the paint chars black over <see cref="CharSeconds"/>.</summary>
    public void Wreck(float turretYaw)
    {
        _wreckTime = 0f;
        _tossYaw = turretYaw;
    }

    /// <summary>Puts the turret back and the paint back to normal (a revived tank).</summary>
    public void Restore()
    {
        _wreckTime = -1f;
        _toss.Transform = new Transform3D(Basis.Identity, new Vector3(0, _tossPath.Pivot.Y, 0));
        SetChar(0f);
    }

    public void Apply(TankPose pose, float turretYaw, float gunPitch)
    {
        GlobalTransform = new Transform3D(new Basis(pose.Rotation.ToGodot()), pose.Position.ToGodot());
        _turret.Rotation = new Vector3(0, turretYaw, 0);
        _gun.Rotation = new Vector3(gunPitch, 0, 0);
    }

    /// <summary>Kicks the barrel back; it returns over <see cref="TankAnimator.RecoilReturnTime"/>.</summary>
    public void Fire(float turretYaw) => Animator.Fire(turretYaw);

    /// <summary>Rolls wheels and tracks, nods the hull, runs recoil and the wreck. Call once per frame after <see cref="Apply"/>.</summary>
    public void Animate(float speed, float yawRate, float delta)
    {
        if (IsWrecked)
        {
            AnimateWreck(delta);
            return;
        }
        Animator.Step(speed, yawRate, delta);
        // The odometer grows without bound; wrap the wheel angle (2π) and the belt offset (belt length) separately, each on its own period.
        foreach (WheelNode wheel in _wheelNodes)
        {
            float rolled = wheel.Left ? Animator.LeftDistance : Animator.RightDistance;
            wheel.Node.Rotation = new Vector3(wheel.Phase - Mathf.PosMod(rolled / wheel.Radius, Mathf.Tau), 0, 0);
        }
        if (IsVisibleInTree()) PlaceLinks(Mathf.PosMod(Animator.LeftDistance, _belt.Length), Mathf.PosMod(Animator.RightDistance, _belt.Length));
        var nod = new Basis(Vector3.Right, Animator.Nod);
        _body.Transform = new Transform3D(nod, _nodPivot - nod * _nodPivot);
        // Anchor at ring height (where the turret sits on the hull) so the nod moves it as the ring moves; only the position follows, orientation stays level.
        var ring = Vector3.Up * _look.TurretBase;
        _turret.Position = _body.Transform * (_turretBase + ring) - ring;
        _barrel.Position = new Vector3(0, 0, Animator.Recoil);
    }

    /// <summary>A wreck doesn't roll or nod: the turret follows its toss path (in hull space) and the paint chars.</summary>
    private void AnimateWreck(float delta)
    {
        _wreckTime += delta;
        var (position, rotation) = _tossPath.At(_wreckTime, _tossYaw);
        _turret.Transform = Transform3D.Identity;   // the toss path already includes the turret's yaw and ring offset
        _toss.Transform = new Transform3D(new Basis(rotation.ToGodot()), position.ToGodot());
        SetChar(Mathf.Clamp(_wreckTime / CharSeconds, 0f, 1f));
    }

    private void SetChar(float amount)
    {
        if (amount == _charShown) return;
        _charShown = amount;
        foreach (ShaderMaterial material in _materials) material.SetShaderParameter("char_amount", amount);
    }

    private ShaderMaterial ToonMaterial(int mode)
    {
        ShaderMaterial material = Materials.FromShader("tank_toon")
            .With("mode", mode)
            .With("body_color", _paint.Body.ToColor())
            .With("hub_color", _paint.Hub.ToColor())
            .With("track_color", _paint.Track.ToColor())
            .With("toon_rim_strength", RimStrength);
        _materials.Add(material);
        return material;
    }

    private void BuildWheels()
    {
        float halfThickness = _look.TrackThickness / 2;
        ShaderMaterial wheelMaterial = ToonMaterial(WheelMode);   // cream hub, track-coloured tyre (vertex-colour mask)
        _belt = new BeltPath(TankParts.TrackLoop(_look));
        _linksPerSide = (int)MathF.Round(_belt.Length / _look.LinkPitch);
        int teeth = TankParts.SprocketTeethCount(_look);
        float linkSpacing = _belt.Length / _linksPerSide;
        // The wheels roll on the belt's centre line (radius + half the link thickness). The sprocket instead turns exactly one
        // tooth per link, so its teeth never drift out of mesh with the links as the odometer grows.
        float sprocketRadius = teeth * linkSpacing / MathF.Tau;
        foreach (bool left in new[] { true, false })
        {
            float x = left ? -_look.TrackX : _look.TrackX;
            ArrayMesh road = TankParts.Wheel(_look.RoadWheelRadius, _look.RoadWheelWidth, left).ToArrayMesh();
            for (int i = 0; i < _look.RoadWheelCount; i++)
                AddWheel(road, new Vector3(x, _look.RoadWheelY, _look.RoadWheelZ(i)), _look.RoadWheelRadius + halfThickness, left, 0f, wheelMaterial);
            WheelSpot s = _look.Sprocket, d = _look.Idler;
            AddWheel(TankParts.Sprocket(s.Radius, _look.RoadWheelWidth, left, teeth).ToArrayMesh(), new Vector3(x, s.Y, s.Z), sprocketRadius, left, SprocketPhase(s, linkSpacing), wheelMaterial);
            AddWheel(TankParts.Wheel(d.Radius, _look.RoadWheelWidth * 0.8f, left).ToArrayMesh(), new Vector3(x, d.Y, d.Z), d.Radius + halfThickness, left, 0f, wheelMaterial);
        }
    }

    /// <summary>
    /// Wheel angle at which a sprocket tooth sits in a gap between two links (the belt offset 0 layout puts link i at
    /// arc length i·spacing, so gaps are at half-steps), measured like the tooth angles: from +Y towards +Z.
    /// </summary>
    private float SprocketPhase(WheelSpot sprocket, float linkSpacing)
    {
        var centre = new System.Numerics.Vector2(sprocket.Z, sprocket.Y);
        var top = centre + new System.Numerics.Vector2(0, sprocket.Radius + _look.TrackThickness / 2);
        float bestArc = 0, best = float.MaxValue;
        for (int i = 0; i < 2000; i++)
        {
            float arc = _belt.Length * i / 2000f;
            float distance = System.Numerics.Vector2.DistanceSquared(_belt.Sample(arc).Position, top);
            if (distance < best) (best, bestArc) = (distance, arc);
        }
        float gapArc = (MathF.Floor(bestArc / linkSpacing) + 0.5f) * linkSpacing;
        System.Numerics.Vector2 offset = _belt.Sample(gapArc).Position - centre;
        return MathF.Atan2(offset.X, offset.Y);
    }

    private void AddWheel(Mesh mesh, Vector3 position, float rollRadius, bool left, float phase, Material material)
    {
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position };
        _wheels.AddChild(node);
        _wheelNodes.Add(new WheelNode(node, rollRadius, left, phase));
    }

    private void BuildTracks(Material material)
    {
        _linkMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = TankParts.TrackLink(_look).ToArrayMesh(),
            InstanceCount = _linksPerSide * 2,
            CustomAabb = TrackBounds(),
        };
        _linkBuffer = new float[_linksPerSide * 2 * LinkFloats];
        _tracks.Multimesh = _linkMesh;
        _tracks.MaterialOverride = material;
        PlaceLinks(0, 0);
    }

    /// <summary>Static bounds of both tracks, so the engine never recomputes them as the links move.</summary>
    private Aabb TrackBounds()
    {
        float minZ = float.MaxValue, maxZ = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < 400; i++)
        {
            var p = _belt.Sample(_belt.Length * i / 400f).Position;
            minZ = MathF.Min(minZ, p.X); maxZ = MathF.Max(maxZ, p.X); maxY = MathF.Max(maxY, p.Y);
        }
        const float Margin = 0.15f; // grousers, link size
        float halfWidth = _look.TrackX + _look.TrackWidth / 2 + Margin;
        return new Aabb(new Vector3(-halfWidth, 0, minZ - Margin), new Vector3(2 * halfWidth, maxY + Margin, maxZ - minZ + 2 * Margin));
    }

    private const int LinkFloats = 12; // Transform3D multimesh layout: three rows of (basis row, origin component)

    private void PlaceLinks(float leftOffset, float rightOffset)
    {
        if (leftOffset == _placedLeft && rightOffset == _placedRight) return;
        (_placedLeft, _placedRight) = (leftOffset, rightOffset);
        float spacing = _belt.Length / _linksPerSide;
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? -_look.TrackX : _look.TrackX;
            float rolled = side == 0 ? leftOffset : rightOffset;
            for (int i = 0; i < _linksPerSide; i++)
            {
                var (p, t) = _belt.Sample(rolled + i * spacing); // (z, y) position and direction of travel
                var outward = new Vector3(0, -t.X, t.Y);          // right of travel on a counter-clockwise loop
                Vector3 across = Vector3.Right, normal = Vector3.Right.Cross(outward);
                int o = (side * _linksPerSide + i) * LinkFloats;
                _linkBuffer[o + 0] = across.X; _linkBuffer[o + 1] = outward.X; _linkBuffer[o + 2] = normal.X; _linkBuffer[o + 3] = x;
                _linkBuffer[o + 4] = across.Y; _linkBuffer[o + 5] = outward.Y; _linkBuffer[o + 6] = normal.Y; _linkBuffer[o + 7] = p.Y;
                _linkBuffer[o + 8] = across.Z; _linkBuffer[o + 9] = outward.Z; _linkBuffer[o + 10] = normal.Z; _linkBuffer[o + 11] = p.X;
            }
        }
        _linkMesh.Buffer = _linkBuffer;
    }
}
