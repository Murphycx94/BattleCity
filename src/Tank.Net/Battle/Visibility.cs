using Tank.Sim;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Net.Battle;

/// <summary>Which tanks one client is told about (design §5.5), decided on the server only.</summary>
/// <param name="Team">The viewer's team: whose shared sight it sees through (in duos 0–7, each player's own in a free-for-all or a royale).</param>
/// <param name="SeesAllWhenDead">
/// Whether the viewer sees everything once dead (or without a tank yet): in a free-for-all and a royale always; in duos only once
/// nobody of its team is left alive (stage 6 design §5.2); never in the two-team modes. While its own tank lives it never does.
/// </param>
public readonly record struct Viewer(byte Team, int OwnTankId, bool OwnDestroyed, bool SeesAllWhenDead)
{
    /// <summary>
    /// A dead player who may watch everything (<see cref="SeesAllWhenDead"/>: a free-for-all, a royale, duos with the whole team out) does;
    /// everyone else, a dead duos player whose team-mate lives included, sees through their team's eyes.
    /// </summary>
    public bool SeesAll => SeesAllWhenDead && (OwnTankId == 0 || OwnDestroyed);

    public bool Sees(in TankState tank, VisionTracker vision)
    {
        if (SeesAll || tank.Id == OwnTankId || tank.Team == Team || tank.IsDestroyed) return true;
        return vision.IsVisible(tank.Id, Team);
    }

    public bool SeesTank(int tankId, Simulation sim, VisionTracker vision)
    {
        int slot = sim.SlotOf(tankId);
        return slot >= 0 && Sees(sim.Tanks[slot], vision);
    }

    /// <summary>
    /// Whether this viewer should get <paramref name="e"/>, and how (design §5.6): shots and bursts always (a shot by a
    /// tank out of sight with the shooter's id blanked), hits and kills when the viewer fired or sees the target,
    /// revivals when it sees the tank, prop changes always; a crate picked never (stage 6 design §4.4: nobody is told of
    /// another's pickup, and the one who ate it gets a <see cref="CrateMessage"/> instead).
    /// </summary>
    public bool Wants(in GameEvent e, Simulation sim, VisionTracker vision, out GameEvent sent)
    {
        if (e.Type == GameEventType.CratePicked)
        {
            sent = default;
            return false;
        }
        // Whoever fired is named only when the viewer may know: own shots, or a tank the viewer sees (design §5.6).
        sent = e.TankId == 0 || e.TankId == OwnTankId || SeesTank(e.TankId, sim, vision) ? e : e with { TankId = 0 };
        switch (e.Type)
        {
            case GameEventType.ShellFired:
            case GameEventType.ShellExploded:
            case GameEventType.PropDamaged:
            case GameEventType.PropDestroyed:
                return true;
            case GameEventType.TankHit:
            case GameEventType.SplashDamage:
            case GameEventType.TankDestroyed:
                return (OwnTankId != 0 && e.TankId == OwnTankId) || SeesTank(e.TargetTankId, sim, vision);
            case GameEventType.TankRevived:
                return SeesTank(e.TargetTankId, sim, vision);
            default:
                return false;
        }
    }

    /// <summary>Whether any enemy team sees the viewer's own tank (the "spotted" lamp).</summary>
    public bool IsSpotted(Simulation sim, VisionTracker vision)
    {
        if (OwnTankId == 0 || OwnDestroyed) return false;
        foreach (ref readonly TankState t in sim.Tanks)
            if (t.Team != Team && !t.IsDestroyed && vision.IsVisible(OwnTankId, t.Team)) return true;
        return false;
    }
}
