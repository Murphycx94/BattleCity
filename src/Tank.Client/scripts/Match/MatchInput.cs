using Godot;
using Tank.Client.Driving;

namespace Tank.Client.Match;

/// <summary>
/// A match's input: everything in <see cref="DriveInput"/> plus Tab held for the scoreboard, F3 to toggle the network
/// panel (both already in project.godot), Enter for the host to leave the results early and the spectating keys Q (previous), E (next), F (free camera)
/// and the left button as another "next", each consumed once by the frame that reads it (only the dead read them).
/// </summary>
public sealed class MatchInput : DriveInput
{
    private static readonly StringName Scoreboard = "scoreboard", ToggleNetPanel = "toggle_netgraph";
    private static readonly StringName ReturnToRoomAction = "return_to_room";
    private static readonly StringName SpectatePrev = "spectate_prev", SpectateNext = "spectate_next", SpectateFree = "spectate_free";

    private int _spectateStep;
    private bool _spectateFree;
    private bool _spectateClick;
    private bool _returnToRoom;

    public static new void RegisterActions()
    {
        DriveInput.RegisterActions();
        AddKey(SpectatePrev, Key.Q);
        AddKey(SpectateNext, Key.E);
        AddKey(SpectateFree, Key.F);
        AddKey(ReturnToRoomAction, Key.Enter);
    }

    /// <summary>Whether Tab is down: the scoreboard shows while it is.</summary>
    public bool ScoreboardHeld => Input.IsActionPressed(Scoreboard);

    /// <summary>Whether the F3 network panel is open; starts as <c>--netgraph</c> says.</summary>
    public bool NetPanelOpen { get; set; }

    /// <summary>Whether Shift is held: the free spectating camera moves three times as fast (the key doubles as the scope toggle, which the dead don't get).</summary>
    public bool SpectateFast => Input.IsActionPressed(SniperToggle);

    public override void OnInputEvent(InputEvent e)
    {
        base.OnInputEvent(e);
        if (e is InputEventMouseButton && e.IsActionPressed(FireGun)) _spectateClick = true;
        if (e is not InputEventKey) return;   // every action below is bound to a key; mouse motion must not pay the lookups
        if (e.IsActionPressed(ToggleNetPanel)) NetPanelOpen = !NetPanelOpen;
        else if (e.IsActionPressed(SpectatePrev)) _spectateStep = -1;
        else if (e.IsActionPressed(SpectateNext)) _spectateStep = 1;
        else if (e.IsActionPressed(SpectateFree)) _spectateFree = true;
        else if (e.IsActionPressed(ReturnToRoomAction)) _returnToRoom = true;
    }

    /// <summary>True once after Enter was pressed (the host's way out of the results screen).</summary>
    public bool TakeReturnToRoom()
    {
        bool pressed = _returnToRoom;
        _returnToRoom = false;
        return pressed;
    }

    /// <summary>Once after Q or E: -1 for the previous spectating target, 1 for the next, 0 for neither.</summary>
    public int TakeSpectateStep()
    {
        int step = _spectateStep;
        _spectateStep = 0;
        return step;
    }

    /// <summary>True once after F was pressed.</summary>
    public bool TakeSpectateFree()
    {
        bool free = _spectateFree;
        _spectateFree = false;
        return free;
    }

    /// <summary>True once after a left click: the spectating "next" (the trigger of the living; the fire latch gets the same click and sends nothing while dead).</summary>
    public bool TakeSpectateClick()
    {
        bool clicked = _spectateClick;
        _spectateClick = false;
        return clicked;
    }
}
