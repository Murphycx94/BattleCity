using Godot;
using Tank.Client.Art;

namespace Tank.Client.Combat;

/// <summary>Colour of an impact's rolling puffs: white smoke (a burst on the ground, a tank blowing up) or grey dust (rock, armour).</summary>
public enum PuffColor { White, Grey }

/// <summary>
/// Cartoon muzzle blasts and shell impacts, built once and reused round-robin (no allocation per shot).
/// Muzzle: a bright yellow flash ball (0.08 s), then 3–5 white puffs pushed out along the barrel one after another,
/// each swelling past full size, settling and shrinking away (~0.8 s).
/// Impact: an orange fireball that balloons and collapses, 6–10 puffs rolling outward (<see cref="PuffColor"/>: white
/// smoke, or grey dust off rock and armour) and 8 small stones thrown up under gravity.
/// Debris (<see cref="Debris"/>): chunks thrown up and along a push (bricks, stones, wood chips, leaves), with an
/// optional cloud of dust puffs; sized for a chip off a wall up to a whole wall coming down.
/// Pool sizes (10 muzzle, 10 impact, 16 debris slots) are sized for a full match (blueprint §5 T10): ten tanks can all
/// fire inside one muzzle life (~0.8 s), so there is a muzzle and an impact slot per possible shooter; debris comes in
/// bursts, up to a dozen at once when an HE shell lands in a bush patch, with headroom for two overlapping. If every
/// slot is busy anyway, the one that started playing longest ago is reused (idle slots always win, being the oldest).
/// Nothing allocates per shot: no tweens, no closures; each slot advances its own flash animation from <see cref="_Process"/>.
/// </summary>
public partial class BattleFx : Node3D
{
    private const int MuzzleSlots = 10;   // one per tank of a full match: all ten can fire inside one muzzle life
    private const int ImpactSlots = 10;   // likewise: their shells can all burst inside one impact life (~1.2 s)
    private const int DebrisSlots = 16;
    private const float MuzzleFlashTime = 0.08f;
    private const float MuzzleFlashSize = 1.3f;
    private const float FireballGrowTime = 0.1f;
    private const float FireballShrinkTime = 0.22f;
    private const float FireballSize = 3.4f;
    private const int MuzzlePuffs = 5;    // 3–5 used per shot
    private const int ImpactPuffs = 10;   // 6–10 used per impact
    private const int Stones = 8;

    private readonly List<MuzzleSlot> _muzzles = new();
    private readonly List<ImpactSlot> _impacts = new();
    private readonly List<DebrisSlot> _debris = new();

    public override void _Ready()
    {
        for (int i = 0; i < MuzzleSlots; i++) _muzzles.Add(new MuzzleSlot(this));
        for (int i = 0; i < ImpactSlots; i++) _impacts.Add(new ImpactSlot(this));
        for (int i = 0; i < DebrisSlots; i++) _debris.Add(new DebrisSlot(this));
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        foreach (MuzzleSlot slot in _muzzles) slot.Advance(dt);
        foreach (ImpactSlot slot in _impacts) slot.Advance(dt);
        foreach (DebrisSlot slot in _debris) slot.Advance(dt);
    }

    /// <summary>The slot that has been idle or playing for the longest time.</summary>
    private static T Oldest<T>(List<T> slots) where T : ISlot
    {
        T best = slots[0];
        foreach (T slot in slots)
            if (slot.Age > best.Age) best = slot;
        return best;
    }

    /// <param name="muzzle">Barrel tip; its −Z axis points along the barrel.</param>
    /// <param name="withSmoke">False for the player's own gun while looking through the sniper sight.</param>
    public void Muzzle(Transform3D muzzle, bool withSmoke = true)
    {
        Oldest(_muzzles).Play(muzzle, withSmoke);
    }

    /// <param name="size">1 for a shell burst; larger for a tank blowing up, smaller for a shell striking armour.</param>
    public void Explosion(Vector3 at, PuffColor puffs, float size = 1f)
    {
        Oldest(_impacts).Play(at, puffs == PuffColor.Grey ? Palette.RockDust.ToColor() : Palette.Smoke.ToColor(), size);
    }

    /// <summary>
    /// Chunks of <paramref name="chunks"/> colour thrown up from <paramref name="at"/> and along <paramref name="push"/>
    /// (level; zero for straight up), with puffs of <paramref name="dust"/> if given. <paramref name="size"/> 1 is a chip
    /// off a wall: about five small chunks; a collapsing wall is about 2, with twice the chunks, bigger and faster.
    /// </summary>
    public void Debris(Vector3 at, Vector3 push, Color chunks, Color? dust, float size)
    {
        Oldest(_debris).Play(at, push, chunks, dust, size);
    }

    private interface ISlot
    {
        /// <summary>Seconds since this slot last started playing (large while idle).</summary>
        float Age { get; }
    }

    /// <summary>A glowing ball with a lamp in it, animated by <see cref="Advance"/> (no tween objects).</summary>
    private sealed class Flash
    {
        private const float BackOvershoot = 1.70158f;   // Godot's TRANS_BACK constant
        private const float StartScale = 0.3f;
        private const float EndScale = 0.01f;
        private const float AgeCap = 10f;   // stop counting once clearly idle

        private readonly MeshInstance3D _ball;
        private readonly OmniLight3D _light;
        private readonly float _lightEnergy;
        private float _time = float.MaxValue;
        private float _size;
        private float _grow;
        private float _shrink;

        public Flash(Node parent, Color color, float energy, float lightEnergy, float lightRange)
        {
            _ball = new MeshInstance3D
            {
                Mesh = ParticleKit.GlowBall(color, energy),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            _light = new OmniLight3D { LightColor = color, LightEnergy = 0, OmniRange = lightRange, ShadowEnabled = false };
            _ball.AddChild(_light);
            parent.AddChild(_ball);
            _lightEnergy = lightEnergy;
        }

        public float Age => _time;

        /// <summary>Grows from 30% to <paramref name="size"/> in <paramref name="grow"/> s (overshooting a little), then shrinks to nothing in <paramref name="shrink"/> s.</summary>
        public void Play(Vector3 at, float size, float grow, float shrink)
        {
            _ball.GlobalPosition = at;
            _size = size;
            _grow = grow;
            _shrink = shrink;
            _time = 0f;
            Apply();
            _ball.Visible = true;
        }

        public void Advance(float dt)
        {
            float total = _grow + _shrink;
            if (_time >= total + AgeCap) return;
            bool wasPlaying = _time < total;
            _time += dt;   // keeps counting after the flash ends: Age tells the pool how long ago this slot started
            if (_time < total) Apply();
            else if (wasPlaying)
            {
                _ball.Visible = false;
                _light.LightEnergy = 0f;
            }
        }

        private void Apply()
        {
            float scale;
            float light;
            if (_time < _grow)
            {
                float x = _time / _grow - 1f;
                float back = 1f + (BackOvershoot + 1f) * x * x * x + BackOvershoot * x * x;   // ease-out back: overshoots, settles at 1
                scale = Mathf.Lerp(StartScale, 1f, back) * _size;
                light = _lightEnergy;
            }
            else
            {
                float x = (_time - _grow) / _shrink;
                scale = Mathf.Lerp(_size, EndScale, x * x);
                light = _lightEnergy * (1f - x);
            }
            _ball.Scale = Vector3.One * scale;
            _light.LightEnergy = light;
        }
    }

    private sealed class MuzzleSlot : ISlot
    {
        private readonly Flash _flash;
        private readonly GpuParticles3D _smoke;

        public MuzzleSlot(Node parent)
        {
            _flash = new Flash(parent, Palette.Flash.ToColor(), 2.5f, 5f, 12f);
            _smoke = ParticleKit.OneShot(MuzzlePuffs, 0.8, 0.7f, new ParticleProcessMaterial
            {
                Direction = new Vector3(0, 0, -1),
                Spread = 12f,
                InitialVelocityMin = 3f,
                InitialVelocityMax = 9f,
                DampingMin = 8f,
                DampingMax = 12f,
                Gravity = new Vector3(0, 0.6f, 0),
                ScaleMin = 1.0f,
                ScaleMax = 1.6f,
                ScaleCurve = ParticleKit.Pop(),
                Color = Palette.Smoke.ToColor(),
            }, ParticleKit.PuffBall);
            parent.AddChild(_smoke);
        }

        public float Age => _flash.Age;

        public void Advance(float dt) => _flash.Advance(dt);

        public void Play(Transform3D muzzle, bool withSmoke)
        {
            _flash.Play(muzzle.Origin - muzzle.Basis.Z * 0.3f, MuzzleFlashSize, MuzzleFlashTime * 0.4f, MuzzleFlashTime * 0.6f);
            _smoke.GlobalTransform = muzzle;
            if (!withSmoke) { _smoke.Emitting = false; return; }
            _smoke.AmountRatio = (3 + GD.Randi() % 3) / (float)MuzzlePuffs;
            _smoke.Restart();
        }
    }

    private sealed class ImpactSlot : ISlot
    {
        private const float PuffScaleMin = 1.1f, PuffScaleMax = 1.9f, PuffSpeedMin = 3.5f, PuffSpeedMax = 7f;

        private readonly Flash _fireball;
        private readonly GpuParticles3D _puffs;
        private readonly ParticleProcessMaterial _puffProcess;
        private readonly GpuParticles3D _stones;

        public ImpactSlot(Node parent)
        {
            _fireball = new Flash(parent, Palette.Fire.ToColor(), 1.3f, 8f, 20f);
            _puffProcess = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.6f,
                Direction = Vector3.Up,
                Spread = 75f,
                InitialVelocityMin = PuffSpeedMin,
                InitialVelocityMax = PuffSpeedMax,
                DampingMin = 5f,
                DampingMax = 7f,
                Gravity = new Vector3(0, 0.4f, 0),
                ScaleMin = PuffScaleMin,
                ScaleMax = PuffScaleMax,
                ScaleCurve = ParticleKit.Pop(start: 0.3f, overshoot: 1.2f, peakAt: 0.15f, shrinkFrom: 0.5f),
                Color = Palette.Smoke.ToColor(),
            };
            _puffs = ParticleKit.OneShot(ImpactPuffs, 1.2, 0.9f, _puffProcess, ParticleKit.PuffBall);
            _stones = ParticleKit.OneShot(Stones, 1.3, 1f, new ParticleProcessMaterial
            {
                Direction = Vector3.Up,
                Spread = 50f,
                InitialVelocityMin = 6f,
                InitialVelocityMax = 11f,
                Gravity = new Vector3(0, -9.8f, 0),
                AngularVelocityMin = -360f,
                AngularVelocityMax = 360f,
                ScaleMin = 0.18f,
                ScaleMax = 0.32f,
                ScaleCurve = ParticleKit.Pop(start: 1f, overshoot: 1f, peakAt: 0.1f, shrinkFrom: 0.8f),
                Color = (Palette.Rock * 0.8f).ToColor(),
            }, ParticleKit.PuffBall);
            parent.AddChild(_puffs);
            parent.AddChild(_stones);
        }

        public float Age => _fireball.Age;

        public void Advance(float dt) => _fireball.Advance(dt);

        public void Play(Vector3 at, Color puffColor, float size)
        {
            _fireball.Play(at + Vector3.Up * 0.6f * size, FireballSize * size, FireballGrowTime, FireballShrinkTime * MathF.Max(1f, size));
            _puffProcess.Color = puffColor;
            _puffProcess.ScaleMin = PuffScaleMin * size;
            _puffProcess.ScaleMax = PuffScaleMax * size;
            _puffProcess.InitialVelocityMin = PuffSpeedMin * size;
            _puffProcess.InitialVelocityMax = PuffSpeedMax * size;
            _puffs.GlobalPosition = at + Vector3.Up * 0.3f;
            _puffs.AmountRatio = (6 + GD.Randi() % 5) / (float)ImpactPuffs;
            _puffs.Restart();
            _stones.GlobalPosition = at + Vector3.Up * 0.3f;
            _stones.Restart();
        }
    }

    private sealed class DebrisSlot : ISlot
    {
        private const int Chunks = 18, Puffs = 12;
        private const float ChunkScale = 0.22f, ChunkSpeed = 4.5f, PuffScale = 1.1f, PuffSpeed = 2.5f;
        private const float Lifetime = 1.6f;
        private const float AgeCap = 10f;

        private readonly GpuParticles3D _chunks;
        private readonly ParticleProcessMaterial _chunkProcess;
        private readonly GpuParticles3D _puffs;
        private readonly ParticleProcessMaterial _puffProcess;
        private float _age = float.MaxValue;

        public DebrisSlot(Node parent)
        {
            _chunkProcess = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.4f,
                Direction = Vector3.Up,
                Spread = 55f,
                Gravity = new Vector3(0, -9.8f, 0),
                AngularVelocityMin = -540f,
                AngularVelocityMax = 540f,
                ScaleCurve = ParticleKit.Pop(start: 1f, overshoot: 1f, peakAt: 0.1f, shrinkFrom: 0.75f),
            };
            _chunks = ParticleKit.OneShot(Chunks, Lifetime, 1f, _chunkProcess, ParticleKit.PuffBox);
            _puffProcess = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                Direction = Vector3.Up,
                Spread = 80f,
                DampingMin = 3f,
                DampingMax = 5f,
                Gravity = new Vector3(0, 0.5f, 0),
                ScaleCurve = ParticleKit.Pop(start: 0.3f, overshoot: 1.15f, peakAt: 0.2f, shrinkFrom: 0.45f),
            };
            _puffs = ParticleKit.OneShot(Puffs, Lifetime, 0.85f, _puffProcess, ParticleKit.PuffBall);
            parent.AddChild(_chunks);
            parent.AddChild(_puffs);
        }

        public float Age => _age;

        public void Advance(float dt)
        {
            if (_age < Lifetime + AgeCap) _age += dt;
        }

        public void Play(Vector3 at, Vector3 push, Color chunks, Color? dust, float size)
        {
            _age = 0f;
            Vector3 level = new(push.X, 0, push.Z);
            _chunkProcess.Direction = (Vector3.Up + (level.LengthSquared() > 1e-6f ? level.Normalized() * 0.7f : Vector3.Zero)).Normalized();
            _chunkProcess.Color = chunks;
            _chunkProcess.ScaleMin = ChunkScale * MathF.Sqrt(size);
            _chunkProcess.ScaleMax = ChunkScale * 1.8f * MathF.Sqrt(size);
            _chunkProcess.InitialVelocityMin = ChunkSpeed * 0.6f * MathF.Sqrt(size);
            _chunkProcess.InitialVelocityMax = ChunkSpeed * 1.4f * MathF.Sqrt(size);
            _chunkProcess.EmissionSphereRadius = 0.3f * size;
            _chunks.GlobalPosition = at;
            _chunks.AmountRatio = Math.Clamp(0.28f * size, 0.2f, 1f);
            _chunks.Restart();
            if (dust is not { } puffColor)
            {
                _puffs.Emitting = false;
                return;
            }
            _puffProcess.Color = puffColor;
            _puffProcess.EmissionBoxExtents = new Vector3(0.9f, 0.6f, 0.9f) * size;
            _puffProcess.ScaleMin = PuffScale * 0.7f * size;
            _puffProcess.ScaleMax = PuffScale * 1.3f * size;
            _puffProcess.InitialVelocityMin = PuffSpeed * 0.5f * size;
            _puffProcess.InitialVelocityMax = PuffSpeed * size;
            _puffs.GlobalPosition = at;
            _puffs.AmountRatio = Math.Clamp(0.25f * size * size, 0.15f, 1f);
            _puffs.Restart();
        }
    }
}
