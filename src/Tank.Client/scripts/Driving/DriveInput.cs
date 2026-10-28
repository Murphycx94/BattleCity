using Godot;
using Tank.Sim.Combat;

namespace Tank.Client.Driving;

/// <summary>
/// The keys and buttons for driving and shooting, shared by the sandbox and a match: W/A/S/D, the left button to fire,
/// the right button for free look, 1 and 2 to pick AP or HE (kept until changed) and Shift for the sniper sight. The
/// actions are registered at runtime (<see cref="RegisterActions"/>), not in project.godot. A fire click shorter
/// than a tick is latched (<see cref="FireLatch"/>); a caller that may see frames without a tick uses <see cref="Peek"/>
/// each frame and <see cref="EndTick"/> only after a tick has run, one that samples once per tick uses <see cref="Sample"/>.
/// </summary>
public class DriveInput
{
    // Action names are cached: a string argument becomes a new StringName on every call, and these are looked up each
    // tick or frame and in the input-event handlers (mouse motion alone arrives about once per frame while aiming).
    private static readonly StringName DriveForward = "drive_forward", DriveBack = "drive_back";
    private static readonly StringName DriveLeft = "drive_left", DriveRight = "drive_right";
    private static readonly StringName FreeLookAction = "free_look";
    private static readonly StringName AmmoAp = "ammo_ap", AmmoHe = "ammo_he";

    // Also read by the scenes' input handlers; UiCancel is Godot's built-in action, not registered here.
    internal static readonly StringName FireGun = "fire_gun", SniperToggle = "sniper_toggle", UiCancel = "ui_cancel";

    /// <summary>M: opens and closes the full-screen map (stage 5 design §5); read by the scenes' input handlers.</summary>
    internal static readonly StringName MapToggle = "map_toggle";

    private readonly FireLatch _fire = new();

    public static void RegisterActions()
    {
        AddKey(DriveForward, Key.W);
        AddKey(DriveBack, Key.S);
        AddKey(DriveLeft, Key.A);
        AddKey(DriveRight, Key.D);
        AddKey(SniperToggle, Key.Shift);
        AddMouse(FireGun, MouseButton.Left);
        AddMouse(FreeLookAction, MouseButton.Right);
        AddKey(AmmoAp, Key.Key1);
        AddKey(AmmoHe, Key.Key2);
        AddKey(MapToggle, Key.M);
    }

    /// <summary>The round the player wants loaded.</summary>
    public AmmoType Ammo { get; private set; } = AmmoType.AP;

    public bool FreeLook => Input.IsActionPressed(FreeLookAction);

    /// <summary>Takes fire and the ammo keys; a subclass overrides this to add its own keys and calls the base first.</summary>
    public virtual void OnInputEvent(InputEvent e)
    {
        if (e.IsActionPressed(FireGun)) _fire.Press();
        else if (e.IsActionReleased(FireGun)) _fire.Release();
        else if (e is InputEventKey)
        {
            // Keyboard events only (both actions are bound to keys): mouse motion, about one per frame while aiming,
            // must not pay the lookups.
            if (e.IsActionPressed(AmmoAp)) Ammo = AmmoType.AP;
            else if (e.IsActionPressed(AmmoHe)) Ammo = AmmoType.HE;
        }
    }

    /// <summary>Back to AP, the round a new tank starts with (after a tank change), until 1 or 2 is pressed again.</summary>
    public void ResetAmmo() => Ammo = AmmoType.AP;

    /// <summary>Drops any pending or held fire (focus lost, so a release may never arrive).</summary>
    public void ReleaseAll() => _fire.Clear();

    /// <summary>The sticks and trigger as they are now; consumes nothing, so it can be called every frame.</summary>
    public (sbyte Throttle, sbyte Steer, bool Fire) Peek()
    {
        sbyte throttle = (sbyte)(Held(DriveForward) - Held(DriveBack));
        sbyte steer = (sbyte)(Held(DriveRight) - Held(DriveLeft));
        return (throttle, steer, _fire.Peek());
    }

    /// <summary>A tick has run with what <see cref="Peek"/> returned: a latched click is spent (a held button stays down).</summary>
    public void EndTick() => _fire.EndTick();

    /// <summary>Peek and EndTick in one, for a caller that samples exactly once per tick.</summary>
    public (sbyte Throttle, sbyte Steer, bool Fire) Sample()
    {
        var cmd = Peek();
        EndTick();
        return cmd;
    }

    private static int Held(StringName action) => Input.IsActionPressed(action) ? 1 : 0;

    protected static void AddKey(StringName action, Key key) => Add(action, new InputEventKey { PhysicalKeycode = key });

    protected static void AddMouse(StringName action, MouseButton button) => Add(action, new InputEventMouseButton { ButtonIndex = button });

    private static void Add(StringName action, InputEvent e)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, e);
    }
}
