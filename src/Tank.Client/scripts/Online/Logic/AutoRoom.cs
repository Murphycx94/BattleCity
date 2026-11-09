using Tank.Net.Battle;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Client.Online;

public enum AutoStepKind { Wait, Done, SetTank, SetMode, SetBots, StartMatch, SetTeam }

/// <summary>One room command to send now (the payload fields that kind uses), or <see cref="AutoStepKind.Wait"/> / <see cref="AutoStepKind.Done"/>.</summary>
public readonly record struct AutoStep(AutoStepKind Kind, string? TankId = null, BattleMode Mode = default, int Bots = 0, byte Team = 0)
{
    public static readonly AutoStep Wait = new(AutoStepKind.Wait);
    public static readonly AutoStep Done = new(AutoStepKind.Done);

    /// <summary>Sends this step's room command; Wait and Done send nothing.</summary>
    public void SendTo(BattleClient client)
    {
        switch (Kind)
        {
            case AutoStepKind.SetTank: client.SetTank(TankId!); break;
            case AutoStepKind.SetMode: client.SetMode(Mode); break;
            case AutoStepKind.SetBots: client.SetBots(Bots); break;
            case AutoStepKind.StartMatch: client.StartMatch(); break;
            case AutoStepKind.SetTeam: client.SetTeam(Team); break;
        }
    }
}

/// <summary>
/// Drives the room from the launch options (<c>--tank</c>, <c>--mode</c>, <c>--team</c>, <c>--bots</c>, <c>--autostart</c>,
/// <c>--players</c>): asked once per frame what to do next, it answers with at most one command, each sent once, in the order
/// tank, mode, team, bots, start. The team goes after the mode (a switch to duos re-teams the room), by host and guest
/// alike, once a session and only outside a match: nothing is sent, and nothing counts as done, until the room is in a mode
/// that has the team (<see cref="ModeRules.TeamsFor"/>) and shows the seat on another one — a room that shows the team
/// already sends nothing, but a later switch that re-teams the seat still gets it asked for. The server ignores a full
/// team. Only the host starts, and only once the room shows what was asked for, its map is ready (a start sent while the
/// server still generates it would be refused, and never sent again) and enough humans are in; when
/// more than one is wanted, that has to hold for <see cref="SettleCalls"/> calls in a row first, so a guest that just
/// came in gets its own <c>--tank</c> to the server before the match starts (the server ignores a tank change once the
/// seat is in the match). A command is never repeated within a round: if the server ignores it (a refused start) the
/// room is left as it is; <see cref="Reset"/> starts the next round, which re-sends the tank only if the room lost it.
/// A map that failed to generate never becomes ready: <see cref="TakeWarning"/> then has one warning for the log.
/// </summary>
public sealed class AutoRoom
{
    /// <summary>How many calls in a row the room has to be ready before a host waiting for others starts: about half a second at 60 frames a second.</summary>
    public const int SettleCalls = 30;

    private readonly string? _tankId;
    private readonly BattleMode? _mode;
    private readonly int? _bots;
    private readonly bool _autoStart;
    private readonly byte? _team;
    private bool _sentTank, _sentMode, _sentTeam, _sentBots, _sentStart;
    private int _readyCalls;   // consecutive calls the room has been ready to start
    private bool _warnedFailedMap;
    private string? _warning;

    /// <param name="tankId">A <c>TankCatalog</c> id to pick, or null to leave the choice alone.</param>
    /// <param name="mode">The mode to set (host only), or null.</param>
    /// <param name="bots">The bot count to set (host only), or null.</param>
    /// <param name="autoStart">Whether the host starts the match by itself.</param>
    /// <param name="team">The team to ask for (0-based, <c>--team</c> less one), or null.</param>
    public AutoRoom(string? tankId, BattleMode? mode, int? bots, bool autoStart, byte? team = null)
    {
        _tankId = tankId;
        _mode = mode;
        _bots = bots;
        _autoStart = autoStart;
        _team = team;
    }

    /// <summary>
    /// A problem the caller should log, once: the room's map failed to generate, so an autostart would wait for ever.
    /// Returns it and clears it; null when there is nothing new.
    /// </summary>
    public string? TakeWarning()
    {
        string? warning = _warning;
        _warning = null;
        return warning;
    }

    /// <summary>Whether the start command has gone out (until <see cref="Reset"/>).</summary>
    public bool Started => _sentStart;

    /// <summary>
    /// Allows the start to be sent again: call when the room is back after a match. Mode and bots stay as sent; the tank
    /// is looked at again and re-sent if the room doesn't show it (a guest's choice that arrived after the match began).
    /// </summary>
    public void Reset()
    {
        _sentStart = false;
        _sentTank = false;
        _readyCalls = 0;
    }

    /// <param name="room">The latest room state, null before the first one arrives.</param>
    /// <param name="slot">The client's own slot.</param>
    /// <param name="humansWanted">How many connected people the room needs before the host starts (<c>--players</c>).</param>
    public AutoStep Next(RoomStateMessage? room, byte slot, bool isHost, int humansWanted)
    {
        if (room is null) return AutoStep.Wait;
        if (_tankId is not null && !_sentTank)
        {
            _sentTank = true;
            if (!HasTank(room, slot)) return new AutoStep(AutoStepKind.SetTank, TankId: _tankId);
        }
        if (!isHost) return TeamStep(room, slot) ?? AutoStep.Done;
        if (_mode is { } mode && !_sentMode)
        {
            _sentMode = true;
            return new AutoStep(AutoStepKind.SetMode, Mode: mode);
        }
        if (TeamStep(room, slot) is { } team) return team;
        if (_bots is { } bots && !_sentBots)
        {
            _sentBots = true;
            return new AutoStep(AutoStepKind.SetBots, Bots: bots);
        }
        if (!_autoStart || _sentStart) return AutoStep.Done;
        if (room.MapState == MapState.Failed && !_warnedFailedMap)
        {
            _warnedFailedMap = true;
            _warning = $"--autostart: the room's map '{room.Map}' failed to generate, so the match can't start.";
        }
        if (!ReadyToStart(room, slot, humansWanted))
        {
            _readyCalls = 0;
            return AutoStep.Wait;
        }
        if (humansWanted > 1 && ++_readyCalls < SettleCalls) return AutoStep.Wait;   // let the newcomer's room commands land first
        _sentStart = true;
        return new AutoStep(AutoStepKind.StartMatch);
    }

    /// <summary>
    /// The team step: null once sent, or with nothing to send now — the room's mode has no such team (yet), the seat isn't
    /// listed, or it is on the team already (none of which marks it sent); <see cref="AutoStep.Wait"/> while a match runs.
    /// </summary>
    private AutoStep? TeamStep(RoomStateMessage room, byte slot)
    {
        if (_team is not { } team || _sentTeam) return null;
        if (team >= ModeRules.TeamsFor(room.Mode)) return null;   // e.g. a duos team in a room not switched to duos yet
        RoomPlayer? me = room.Players.FirstOrDefault(p => p.Slot == slot);
        if (me is null || me.Team == team) return null;
        if (room.MatchRunning) return AutoStep.Wait;
        _sentTeam = true;
        return new AutoStep(AutoStepKind.SetTeam, Team: team);
    }

    private bool ReadyToStart(RoomStateMessage room, byte slot, int humansWanted)
    {
        if (room.MatchRunning || room.MapState != MapState.Ready) return false;
        if (_mode is { } mode && room.Mode != mode) return false;
        if (_bots is { } bots && room.Bots != bots) return false;
        if (_tankId is not null && !HasTank(room, slot)) return false;
        return room.Players.Count(p => p.Connected && !p.IsBot) >= humansWanted;
    }

    private bool HasTank(RoomStateMessage room, byte slot)
    {
        RoomPlayer? me = room.Players.FirstOrDefault(p => p.Slot == slot);
        return me is not null && me.TankIndex < TankCatalog.All.Count && TankCatalog.All[me.TankIndex].Id == _tankId;
    }
}
