using Godot;

namespace Tank.Client.Match;

/// <summary>
/// A full-screen, click-through control that draws one block of the match HUD. It knows where every block goes
/// (<see cref="Boxes"/>, from <see cref="HudLayout"/> for the control's current size) and redraws itself when the
/// window is resized; the block itself redraws only when its content changes (its subclass calls <c>QueueRedraw</c>).
/// </summary>
public abstract partial class HudPanel : Control
{
    protected HudBoxes Boxes { get; private set; }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += OnResized;
        ReadBoxes();
    }

    private void OnResized()
    {
        ReadBoxes();
        QueueRedraw();
    }

    private void ReadBoxes() => Boxes = HudLayout.For(new System.Numerics.Vector2(Size.X, Size.Y));

    protected static Rect2 ToRect(in HudRect r) => new(r.X, r.Y, r.Width, r.Height);
}
