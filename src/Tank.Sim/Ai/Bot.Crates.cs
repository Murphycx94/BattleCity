using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Ai;

/// <summary>
/// Fetching supply crates in a royale (stage 6 design §4.5; stage 6B plan, task 6). Every <see cref="VisionTracker.UpdateInterval"/>
/// ticks (spread by tank id, as the spotting is: plan, deviation 16) a bot that is wandering (a <see cref="Leg.Waypoint"/>, a
/// wander <see cref="Leg.Zone"/> or <see cref="Leg.Arrived"/>), neither running the zone nor hurrying, and that had neither a
/// target nor an enemy too close the tick before, asks the director for a crate (<see cref="BotDirector.CrateFor"/>) and
/// drives for it (<see cref="Leg.Crate"/>), to stop on it: the hull over the crate eats it (<see cref="CrateSet.Collect"/>).
/// On the way it stops to fight as ever and goes on afterwards (a crate leg isn't one it keeps to under fire). The run is
/// over (<see cref="CrateRunOver"/>) when the crate is gone (eaten by it, or by someone else first), when the tank has
/// stopped on the crate's spot (or at the end of a whole path to it) without eating it, or after
/// <see cref="BotDirector.CrateGiveUpTicks"/>; a search that finds no way to it, or getting stuck three times on the way, ends
/// it too: then on to the next waypoint (<see cref="EndCrateRun"/>). A crate given up that way, still standing, is shunned
/// (<see cref="ShunCrate"/>: a bit in a mask the director's choice skips) for <see cref="BotDirector.CrateShunTicks"/> from
/// the last give-up, or until the next ring (batch C review follow-up: picked again at the next look, a crate boxed in by
/// rocks or parked tanks held the bot where it stood, one flood search after another). A new ring drops a crate outside it
/// (Bot.Zone.cs), and a bot out of its ring runs for the ring as ever, the crate forgotten. No random numbers: the old seeds
/// replay tick for tick.
/// </summary>
public sealed partial class Bot
{
    private int _crate = -1;      // Leg.Crate: the crate's id; −1 off a crate leg (read on one only)
    private int _crateBegun;      // Leg.Crate: the tick the run began
    private ulong _cratesShunned; // crates given up, not picked again for now: bit = crate id (CrateRules.MaxCrates = 64)
    private int _shunUntil;       // the tick the shunned crates are forgotten (CrateShunTicks after the last give-up)

    /// <summary>Crate runs begun this match.</summary>
    public int CrateRuns { get; private set; }

    /// <summary>Crates it ran for and ate this match (one driven over by chance isn't counted here; <see cref="CrateSet.TakenBy"/> has them all).</summary>
    public int CratesTaken { get; private set; }

    /// <summary>
    /// Once a command, after the arrival check and before the target is picked: on its look (every
    /// <see cref="VisionTracker.UpdateInterval"/> ticks, spread by tank id), the shunned crates forgotten once their time is
    /// up, a wandering bot with nothing to fight begins a crate run when the director finds it a crate it doesn't shun.
    /// </summary>
    private void WatchCrates(BotDirector director, in TankState me, int tick)
    {
        CrateSet? crates = director.Crates;
        if (crates is null || (tick + TankId) % VisionTracker.UpdateInterval != 0) return;
        if (_cratesShunned != 0 && tick >= _shunUntil) _cratesShunned = 0;
        if (RunningToZone || ZoneUrgent || Picker.TargetId != 0 || Picker.TooClose != 0 || _leg is not (Leg.Waypoint or Leg.Zone or Leg.Arrived)) return;
        int id = director.CrateFor(me, _cratesShunned);
        if (id < 0) return;
        (_crate, _crateBegun, CrateRuns) = (id, tick, CrateRuns + 1);
        Begin(Leg.Crate, crates[id].Position, keepPath: false);
    }

    /// <summary>
    /// On a <see cref="Leg.Crate"/>: the crate is gone, the tank has stopped on its spot (or at the end of a whole path to
    /// it: the spot off open ground, say), or the run has outstayed <see cref="BotDirector.CrateGiveUpTicks"/>.
    /// </summary>
    private bool CrateRunOver(in TankState me, int tick)
    {
        CrateSet crates = _director.Crates!;   // a crate leg begins only with crates
        return !crates.IsStanding(_crate) || Steering.StoppedAt(me, Goal) || (WholePath && Steering.StoppedAt(me, _path[_pathCount - 1])) ||
               tick - _crateBegun >= BotDirector.CrateGiveUpTicks;
    }

    /// <summary>
    /// The crate run is over, however it ended: the crate counted if this bot ate it, shunned if it still stands (given
    /// up); then on to the next waypoint.
    /// </summary>
    private void EndCrateRun(in TankState me, int tick)
    {
        CrateSet? crates = _director.Crates;
        if (crates is not null)
        {
            if (crates.IsStanding(_crate)) ShunCrate(_crate, tick);
            else if (crates.TakenBy(_crate) == TankId) CratesTaken++;
        }
        _crate = -1;
        NextWaypoint(me);
    }

    /// <summary>
    /// Crate <paramref name="id"/> given up: not picked by this bot again before <see cref="BotDirector.CrateShunTicks"/>
    /// from now (every crate shunned waits for the last give-up's), or the next ring (Bot.Zone.cs: a crate boxed in by
    /// parked tanks may be free by then).
    /// </summary>
    private void ShunCrate(int id, int tick) => (_cratesShunned, _shunUntil) = (_cratesShunned | 1ul << id, tick + BotDirector.CrateShunTicks);

    /// <summary>A crate leg dropped for something else (the ring, a revival): the crate forgotten, not shunned.</summary>
    private void DropCrate() => _crate = -1;
}
