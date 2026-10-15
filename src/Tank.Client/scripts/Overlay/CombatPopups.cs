using Godot;
using Tank.Client.Art;
using Tank.Sim;

namespace Tank.Client.Overlay;

/// <summary>
/// Big outlined cartoon words where a shell strikes a tank — red "击穿！", yellow "跳弹！", grey "未击穿" — and damage
/// numbers ("-220") floating up from the target's head (splash damage a size smaller). A verdict sits level with the
/// point struck, just right of it, so neither the reticle (usually right on that point) nor the target's health bar
/// (over its head, drawn above the popups) covers it; a damage number starts just above the health bar. Anchored in the
/// world and drawn in screen space; pooled slots animated by <see cref="PopupCurves"/>, no tweens. When every slot is
/// busy the oldest is reused.
/// </summary>
public partial class CombatPopups : Control
{
    private const int Slots = 24;
    private const int VerdictSize = 46;
    private const int DamageSize = 38;
    private const int SplashSize = 30;
    private const float VerdictLift = 0.6f;   // a verdict drifts up this far over its life, m
    private const float VerdictBeside = 42f;   // pixels from the point struck to a verdict's left end, clear of the reticle's rings
    private const float DamageAbove = 24f;     // pixels from the head point up to a damage number's middle, clear of the health bar

    private readonly Popup[] _popups = new Popup[Slots];
    private Camera3D? _camera;

    private struct Popup
    {
        public bool Active;
        public bool IsDamage;
        public Vector3 World;
        public string Text;
        public Color Color;
        public int Size;
        public float Age;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void SetCamera(Camera3D camera) => _camera = camera;

    /// <summary>The verdict for a <see cref="GameEventType.TankHit"/>, at the point struck.</summary>
    public void Verdict(Vector3 at, HitResult result)
    {
        (string text, Color color) = result switch
        {
            HitResult.Penetrated => ("击穿！", Palette.HitPierced.ToColor()),
            HitResult.Ricochet => ("跳弹！", Palette.UiAccent.ToColor()),
            _ => ("未击穿", Palette.HitBlocked.ToColor()),
        };
        Spawn(new Popup { World = at, Text = text, Color = color, Size = VerdictSize });
    }

    /// <summary>A damage number rising from <paramref name="head"/> (above the target).</summary>
    public void Damage(Vector3 head, int damage, bool splash)
    {
        Spawn(new Popup
        {
            IsDamage = true,
            World = head,
            Text = $"-{damage}",
            Color = Palette.HitPierced.ToColor(),
            Size = splash ? SplashSize : DamageSize,
        });
    }

    private void Spawn(Popup popup)
    {
        int slot = 0;
        for (int i = 0; i < Slots; i++)
        {
            if (!_popups[i].Active) { slot = i; break; }
            if (_popups[i].Age > _popups[slot].Age) slot = i;
        }
        popup.Active = true;
        _popups[slot] = popup;
    }

    public override void _Process(double delta)
    {
        bool any = false;
        for (int i = 0; i < Slots; i++)
        {
            ref Popup p = ref _popups[i];
            if (!p.Active) continue;
            p.Age += (float)delta;
            p.Active = p.Age < (p.IsDamage ? PopupCurves.DamageLife : PopupCurves.VerdictLife);
            any = true;
        }
        if (any) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_camera is null) return;
        for (int i = 0; i < Slots; i++)
        {
            ref Popup p = ref _popups[i];
            if (!p.Active) continue;
            float life = p.IsDamage ? PopupCurves.DamageLife : PopupCurves.VerdictLife;
            float rise = p.IsDamage ? PopupCurves.Rise(p.Age) : VerdictLift * p.Age / life;
            Vector3 world = p.World + Vector3.Up * rise;
            if (_camera.IsPositionBehind(world)) continue;
            float scale = p.IsDamage ? PopupCurves.DamageScale(p.Age) : PopupCurves.VerdictScale(p.Age);
            var color = new Color(p.Color, PopupCurves.Alpha(p.Age, life));
            Vector2 at = _camera.UnprojectPosition(world);
            float baseline = HudStyle.Baseline(0f, p.Size);
            if (p.IsDamage)
            {
                DrawSetTransform(at - new Vector2(0, DamageAbove), 0f, new Vector2(scale, scale));
                HudStyle.DrawOutlinedCentered(this, 0f, baseline, p.Text, p.Size, color);
            }
            else
            {
                DrawSetTransform(at + new Vector2(VerdictBeside, 0), 0f, new Vector2(scale, scale));   // pops out from its left end
                HudStyle.DrawOutlined(this, new Vector2(0f, baseline), p.Text, p.Size, color);
            }
        }
        DrawSetTransform(Vector2.Zero);
    }
}
