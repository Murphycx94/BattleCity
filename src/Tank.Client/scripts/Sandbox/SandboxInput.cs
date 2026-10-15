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
    private bool _fireLatched;
    private bool _fireHeld;
    private bool _restock;

    public static void RegisterActions()
    {
        AddKey("drive_forward", Key.W);
        AddKey("drive_back", Key.S);
        AddKey("drive_left", Key.A);
        AddKey("drive_right", Key.D);
        AddKey("sniper_toggle", Key.Shift);
        AddMouse("fire_gun", MouseButton.Left);
        AddMouse("free_look", MouseButton.Right);
        AddKey("ammo_ap", Key.Key1);
        AddKey("ammo_he", Key.Key2);
        AddKey("restock", Key.R);
    }

    /// <summary>The round the player wants loaded.</summary>
    public AmmoType Ammo { get; private set; } = AmmoType.AP;

    public bool FreeLook => Input.IsActionPressed("free_look");

    public void OnInputEvent(InputEvent e)
    {
        if (e.IsActionPressed("fire_gun")) _fireLatched = _fireHeld = true;
        else if (e.IsActionReleased("fire_gun")) _fireHeld = false;
        else if (e.IsActionPressed("ammo_ap")) Ammo = AmmoType.AP;
        else if (e.IsActionPressed("ammo_he")) Ammo = AmmoType.HE;
        else if (e.IsActionPressed("restock")) _restock = true;
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
        sbyte throttle = (sbyte)(Held("drive_forward") - Held("drive_back"));
        sbyte steer = (sbyte)(Held("drive_right") - Held("drive_left"));
        bool fire = _fireLatched || _fireHeld;
        _fireLatched = false;
        return (throttle, steer, fire);
    }

    private static int Held(string action) => Input.IsActionPressed(action) ? 1 : 0;

    private static void AddKey(string action, Key key) => Add(action, new InputEventKey { PhysicalKeycode = key });

    private static void AddMouse(string action, MouseButton button) => Add(action, new InputEventMouseButton { ButtonIndex = button });

    private static void Add(string action, InputEvent e)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, e);
    }
}
