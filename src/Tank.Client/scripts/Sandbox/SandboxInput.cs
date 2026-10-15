using Godot;
using Tank.Sim.Combat;

namespace Tank.Client.Sandbox;

/// <summary>
/// Registers the sandbox's own input actions at runtime (so project.godot and the old game's bindings stay untouched)
/// and samples them once per sim tick. A fire click shorter than a tick is latched so it is never lost. 1 and 2 pick
/// AP or HE (kept until changed); R asks for a restock, consumed once by the next tick.
/// </summary>
public sealed class SandboxInput
{
    // Action names are cached: a string argument becomes a new StringName on every call, and these are looked up each
    // tick or frame and in the input-event handlers (mouse motion alone arrives about once per frame while aiming).
    private static readonly StringName DriveForward = "drive_forward", DriveBack = "drive_back";
    private static readonly StringName DriveLeft = "drive_left", DriveRight = "drive_right";
    private static readonly StringName FreeLookAction = "free_look";
    private static readonly StringName AmmoAp = "ammo_ap", AmmoHe = "ammo_he", Restock = "restock";

    // Also read by SandboxMain's input handlers; UiCancel is Godot's built-in action, not registered here.
    internal static readonly StringName FireGun = "fire_gun", SniperToggle = "sniper_toggle", UiCancel = "ui_cancel";

    private bool _fireLatched;
    private bool _fireHeld;
    private bool _restock;

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
        AddKey(Restock, Key.R);
    }

    /// <summary>The round the player wants loaded.</summary>
    public AmmoType Ammo { get; private set; } = AmmoType.AP;

    public bool FreeLook => Input.IsActionPressed(FreeLookAction);

    public void OnInputEvent(InputEvent e)
    {
        if (e.IsActionPressed(FireGun)) _fireLatched = _fireHeld = true;
        else if (e.IsActionReleased(FireGun)) _fireHeld = false;
        else if (e.IsActionPressed(AmmoAp)) Ammo = AmmoType.AP;
        else if (e.IsActionPressed(AmmoHe)) Ammo = AmmoType.HE;
        else if (e.IsActionPressed(Restock)) _restock = true;
    }

    /// <summary>Drops any pending or held fire (focus lost, so a release may never arrive).</summary>
    public void ReleaseAll()
    {
        _fireLatched = false;
        _fireHeld = false;
    }

    /// <summary>True once after R was pressed.</summary>
    public bool TakeRestock()
    {
        bool restock = _restock;
        _restock = false;
        return restock;
    }

    public (sbyte Throttle, sbyte Steer, bool Fire) Sample()
    {
        sbyte throttle = (sbyte)(Held(DriveForward) - Held(DriveBack));
        sbyte steer = (sbyte)(Held(DriveRight) - Held(DriveLeft));
        bool fire = _fireLatched || _fireHeld;
        _fireLatched = false;
        return (throttle, steer, fire);
    }

    private static int Held(StringName action) => Input.IsActionPressed(action) ? 1 : 0;

    private static void AddKey(StringName action, Key key) => Add(action, new InputEventKey { PhysicalKeycode = key });

    private static void AddMouse(StringName action, MouseButton button) => Add(action, new InputEventMouseButton { ButtonIndex = button });

    private static void Add(StringName action, InputEvent e)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, e);
    }
}
