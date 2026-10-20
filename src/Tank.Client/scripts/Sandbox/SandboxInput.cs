using Godot;
using Tank.Client.Driving;

namespace Tank.Client.Sandbox;

/// <summary>
/// The sandbox's input: everything in <see cref="DriveInput"/> plus V (read by <c>SandboxMain</c>) to show or hide the
/// spotting debug lines, R to ask for a restock, T to restore the walls, fences and bushes, and F1–F4 for another tank,
/// each consumed once by the next tick.
/// </summary>
public sealed class SandboxInput : DriveInput
{
    private static readonly StringName Restock = "restock", RestoreProps = "restore_props";

    // The tank-pick keys, one row per action so the action and its key can't drift apart; the row index is the
    // TankCatalog.All index that TakeTankPick returns.
    private static readonly (StringName Action, Key Key)[] PickTank =
    {
        ("pick_tank_1", Key.F1), ("pick_tank_2", Key.F2), ("pick_tank_3", Key.F3), ("pick_tank_4", Key.F4),
    };

    // Also read by SandboxMain's input handler.
    internal static readonly StringName SpotDebug = "spot_debug";

    private bool _restock;
    private bool _restoreProps;
    private int _pickedTank = -1;

    public static new void RegisterActions()
    {
        DriveInput.RegisterActions();
        AddKey(SpotDebug, Key.V);
        AddKey(Restock, Key.R);
        AddKey(RestoreProps, Key.T);
        foreach ((StringName action, Key key) in PickTank) AddKey(action, key);
    }

    public override void OnInputEvent(InputEvent e)
    {
        base.OnInputEvent(e);
        if (e is not InputEventKey) return;   // every action below is bound to a key; mouse motion must not pay the lookups
        if (e.IsActionPressed(Restock)) _restock = true;
        else if (e.IsActionPressed(RestoreProps)) _restoreProps = true;
        else
        {
            for (int i = 0; i < PickTank.Length; i++)
                if (e.IsActionPressed(PickTank[i].Action)) _pickedTank = i;
        }
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
}
