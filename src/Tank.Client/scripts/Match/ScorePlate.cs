using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;
using Tank.Client.UI;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The plate at the top centre (blueprint §4.2): the clock in the middle with a score block to either side (a small
/// caption over a big value, the words per mode from <see cref="HudText"/>), and the mode's name in small type under it.
/// The clock turns red in its last half minute. Redraws only when the clock's second, a score or the mode changes.
/// </summary>
public partial class ScorePlate : HudPanel
{
    private const int ClockSize = 40, ValueSize = 36, CaptionSize = 16, LabelSize = 18;
    private const int UrgentSeconds = 30;
    private const float CaptionBaseline = 24f, ValueBaseline = 60f;   // inside the plate, from its top
    private const float DividerInset = 14f;
    private const int PlateRadius = 20;
    private const float PlateAlpha = 0.85f;

    private readonly StyleBoxFlat _box = HudStyle.RoundedBox(new Color(HudStyle.Panel, PlateAlpha), PlateRadius, 8);
    private int _seconds = -1;
    private PlateValues _plate;
    private bool _hasPlate;
    private BattleMode _mode;
    private bool _hasMode;
    private string _clock = "";
    private string _label = "";
    private PlateParts _parts;
    private Color _leftColor, _rightColor;

    public void Update(in MatchHudFrame frame)
    {
        int seconds = HudText.CeilSeconds(frame.TicksLeft);
        bool changed = false;
        if (seconds != _seconds)
        {
            _seconds = seconds;
            _clock = HudText.Clock(seconds);
            changed = true;
        }
        if (!_hasPlate || frame.Plate != _plate)
        {
            _plate = frame.Plate;
            _hasPlate = true;
            _parts = HudText.Parts(_plate);
            (_leftColor, _rightColor) = SideColors(_plate.Kind);
            changed = true;
        }
        if (!_hasMode || frame.Mode != _mode)
        {
            _mode = frame.Mode;
            _hasMode = true;
            _label = ModeText.Name(_mode);
            changed = true;
        }
        if (changed) QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_hasPlate) return;
        HudRect plate = Boxes.ScorePlate;
        DrawStyleBox(_box, ToRect(plate));
        float middle = plate.CenterX;
        float leftX = plate.X + HudLayout.PlateSideWidth / 2, rightX = plate.Right - HudLayout.PlateSideWidth / 2;
        Color clockColor = _seconds <= UrgentSeconds ? Palette.UiDanger.ToColor() : HudStyle.Text;
        HudStyle.DrawOutlinedCentered(this, middle, plate.Y + HudStyle.Baseline(HudLayout.PlateHeight / 2, ClockSize), _clock, ClockSize, clockColor);
        DrawBlock(leftX, plate.Y, _parts.LeftCaption, _parts.LeftValue, _leftColor);
        DrawBlock(rightX, plate.Y, _parts.RightCaption, _parts.RightValue, _rightColor);
        var divider = new Color(HudStyle.OutlineColor, 0.7f);
        float top = plate.Y + DividerInset, bottom = plate.Bottom - DividerInset;
        DrawLine(new Vector2(plate.X + HudLayout.PlateSideWidth, top), new Vector2(plate.X + HudLayout.PlateSideWidth, bottom), divider, 2f);
        DrawLine(new Vector2(plate.Right - HudLayout.PlateSideWidth, top), new Vector2(plate.Right - HudLayout.PlateSideWidth, bottom), divider, 2f);
        HudStyle.DrawOutlinedCentered(this, middle, Boxes.ModeLabel.Y + HudStyle.Baseline(HudLayout.ModeLabelHeight / 2, LabelSize), _label, LabelSize, HudStyle.Text, 4);
    }

    private void DrawBlock(float centerX, float top, string caption, string value, Color color)
    {
        HudStyle.DrawOutlinedCentered(this, centerX, top + CaptionBaseline, caption, CaptionSize, Palette.UiDim.ToColor(), 4);
        HudStyle.DrawOutlinedCentered(this, centerX, top + ValueBaseline, value, ValueSize, color);
    }

    /// <summary>The player's side in blue-grey and the enemy's in red for the team modes; the player's kills in the accent colour for a free-for-all.</summary>
    private static (Color Left, Color Right) SideColors(PlateKind kind) => kind == PlateKind.SoloKills
        ? (HudStyle.Accent, HudStyle.Text)
        : (MatchColors.Ui(Side.Ally).ToColor(), MatchColors.Ui(Side.Enemy).ToColor());
}
