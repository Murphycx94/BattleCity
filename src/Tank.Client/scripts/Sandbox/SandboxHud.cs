using Godot;
using Tank.Client.Cameras;
using Tank.Client.Overlay;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Client.Sandbox;

/// <summary>
/// The sandbox's HUD: the <see cref="CombatHud"/> (vignette, popups, wall bars, reticle, status panel with the spotted
/// lamp) plus the targets' health bars and the spotting debug lines (V, or --spot-debug), drawn between the bars and the
/// reticle. A target the player's team doesn't see gets no bar, verdict or damage number.
/// </summary>
public partial class SandboxHud : CombatHud
{
    private TargetRange _targets = null!;
    private VisionTracker _vision = null!;
    private SpotLabels _spotLabels = null!;
    private bool _spotDebug;
    private int _labelsTick = -1;   // the simulation tick the debug lines were last worked out for

    /// <param name="playerId">The player's tank: only its shots get verdicts and damage numbers.</param>
    public void Build(OrbitCamera camera, TargetRange targets, Battlefield field, VisionTracker vision, int playerId, bool spotDebug)
    {
        (_targets, _vision, _spotDebug) = (targets, vision, spotDebug);
        Build(camera, field, playerId, targets.Count, TryGetHead);   // the method group is converted once, here
    }

    protected override void AddBehindSight()
    {
        _spotLabels = Add(new SpotLabels());
        _spotLabels.Setup(Cam.Camera, _targets.Count);
        _spotLabels.Visible = _spotDebug;
    }

    /// <summary>A target's head, unless the player's team doesn't see it now (or it isn't a target).</summary>
    private bool TryGetHead(int tankId, out Vector3 head)
    {
        head = Vector3.Zero;
        return !_targets.IsHidden(tankId) && _targets.TryGetHead(tankId, out head);
    }

    /// <summary>Shows or hides the spotting debug lines.</summary>
    public void ToggleSpotDebug() => _spotLabels.Visible = !_spotLabels.Visible;

    /// <summary>
    /// Once per frame, after the targets have been synced: the combat HUD, the targets' bars and the debug lines (their
    /// looks worked out once per simulation <paramref name="tick"/>).
    /// </summary>
    public void Refresh(in TankState player, int tick)
    {
        Refresh(player, spotted: !player.IsDestroyed && _vision.IsVisible(player.Id, TargetRange.TargetTeam));
        bool look = _spotLabels.Visible && tick != _labelsTick;
        for (int i = 0; i < _targets.Count; i++)
        {
            var (head, health, alive, shown) = _targets.Status(i);
            TankBars.Set(i, head, health, alive && shown);
            if (!_spotLabels.Visible) continue;
            _spotLabels.Place(i, head, hasLine: alive);
            if (look) _spotLabels.Set(i, _vision.Inspect(player.Id, _targets.IdAt(i)), drawn: shown);
        }
        if (look) _labelsTick = tick;
    }
}
