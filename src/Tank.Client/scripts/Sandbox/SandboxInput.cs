using Godot;
using Tank.Sim.Combat;

namespace Tank.Client.Sandbox;

/// <summary>
/// Registers the sandbox's own input actions at runtime (so project.godot and the old game's bindings stay untouched)
/// and samples them once per sim tick. A fire click shorter than a tick is latched so it is never lost. 1 and 2 pick
/// AP or HE (kept until changed); R asks for a restock, T to restore the walls, fences and bushes, and F1–F4 for another
/// tank, each consumed once by the next tick.
/// </summary>
public sealed class SandboxInput
{
    // Action names are cached: a string argument becomes a new StringName on every call, and these are looked up each
    // tick or frame and in the input-event handlers (mouse motion alone arrives about once per frame while aiming).
    private static readonly StringName DriveForward = "drive_forward", DriveBack = "drive_back";
    private static readonly StringName DriveLeft = "drive_left", DriveRight = "drive_right";
    private static readonly StringName FreeLookAction = "free_look";
    private static readonly StringName AmmoAp = "ammo_ap", AmmoHe = "ammo_he", Restock = "restock", RestoreProps = "restore_props";

    // The tank-pick keys, one row per action so the action and its key can't drift apart; the row index is the
    // TankCatalog.All index that TakeTankPick returns.
    private static readonly (StringName Action, Key Key)[] PickTank =
    {
        ("pick_tank_1", Key.F1), ("pick_tank_2", Key.F2), ("pick_tank_3", Key.F3), ("pick_tank_4", Key.F4),
    };

    // Also read by SandboxMain's input handlers; UiCancel is Godot's built-in action, not registered here.
    internal static readonly StringName FireGun = "fire_gun", SniperToggle = "sniper_toggle", UiCancel = "ui_cancel";

    private bool _fireLatched;
    private bool _fireHeld;
    private bool _restock;
    private bool _restoreProps;
    private int _pickedTank = -1;

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
        AddKey(RestoreProps, Key.T);
        foreach ((StringName action, Key key) in PickTank) AddKey(action, key);
    }

    /// <summary>The round the player wants loaded.</summary>
    public AmmoType Ammo { get; private set; } = AmmoType.AP;

    public bool FreeLook => Input.IsActionPressed(FreeLookAction);

    public void OnInputEvent(InputEvent e)
    {
        if (e.IsActionPressed(FireGun)) _fireLatched = _fireHeld = true;
        else if (e.IsActionReleased(FireGun)) _fireHeld = false;
        else if (e is InputEventKey)
        {
            // Keyboard events only (every action below is bound to a key): mouse motion, about one per frame while aiming,
            // must not pay their eight lookups.
            if (e.IsActionPressed(AmmoAp)) Ammo = AmmoType.AP;
            else if (e.IsActionPressed(AmmoHe)) Ammo = AmmoType.HE;
            else if (e.IsActionPressed(Restock)) _restock = true;
            else if (e.IsActionPressed(RestoreProps)) _restoreProps = true;
            else
            {
                for (int i = 0; i < PickTank.Length; i++)
                    if (e.IsActionPressed(PickTank[i].Action)) _pickedTank = i;
            }
        }
    }

    /// <summary>Back to AP, the round a new tank starts with (after a tank change), until 1 or 2 is pressed again.</summary>
    public void ResetAmmo() => Ammo = AmmoType.AP;

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

    /// <summary>True once after T was pressed.</summary>
    public bool TakeRestoreProps()
    {
        bool restore = _restoreProps;
        _restoreProps = false;
        return restore;
    }

    /// <summary>Once after F1–F4 was pressed: the tank asked for, as an index into <c>TankCatalog.All</c>.</summary>
    public int? TakeTankPick()
    {
        int picked = _pickedTank;
        _pickedTank = -1;
        return picked < 0 ? null : picked;
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
