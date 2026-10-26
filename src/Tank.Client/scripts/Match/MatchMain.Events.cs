using Godot;
using Tank.Client.Combat;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Net.Battle;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// Events to effects (blueprint §3.3). Another tank's shot flashes where the event says, along the shell's flight, with
/// the recoil if the tank is in sight (shooter id 0 is a shooter the server hid: the flash alone). A burst over the
/// ground is dust off rock or armour, on it white smoke; an AP strike a small grey puff. Kills blow up and wreck the
/// actor, the own tank's included; revivals put it back (the state's fallback is in <see cref="WreckTracker"/>; the
/// own tank's revival is the state's at once — <c>SyncDeath</c> — so its event usually finds nothing left to do). The
/// combat HUD, the walls and the prop effects get every event, as in the sandbox.
/// </summary>
public partial class MatchMain
{
    private const float ArmorHitSize = 0.5f;        // an AP shell striking armour: a small grey puff
    private const float GroundBurstHeight = 0.5f;   // a shell bursting higher than this over the ground hit a rock or a tank
    private const float BlastSize = 1.8f;           // a tank blowing up
    private const float BlastHeight = 1.2f;
    private const float FireShake = 0.08f;
    private const float FireShakeSeconds = 0.15f;
    private const string UnknownName = "未知玩家";

    private void OnEvent(in GameEvent e)
    {
        Battlefield field = _field!;
        _automation.OnEvent(e, _client.OwnTankId, _time);
        if (e.Type == GameEventType.TankDestroyed) FeedKill(e);
        switch (e.Type)
        {
            case GameEventType.ShellFired:
                OnShellFired(e);
                break;
            case GameEventType.ShellExploded:
                bool aboveGround = e.Position.Y > field.Terrain.HeightAt(e.Position.X, e.Position.Z) + GroundBurstHeight;
                _fx.ShellBurst(e.Position.ToGodot(), aboveGround ? PuffColor.Grey : PuffColor.White);
                break;
            case GameEventType.TankHit when e.Ammo == AmmoType.AP:   // HE gets its own ShellExploded
                _fx.Explosion(e.Position.ToGodot(), PuffColor.Grey, ArmorHitSize);
                break;
            case GameEventType.TankDestroyed when e.TargetTankId == _client.OwnTankId:
                _fx.Explosion(e.Position.ToGodot() + Vector3.Up * BlastHeight, PuffColor.White, BlastSize);
                if (_tank is not null && _ownWreck.Destroyed() == WreckTracker.Change.Wreck) _tank.Wreck(_client.Own.TurretYaw);
                break;
            case GameEventType.TankRevived when e.TargetTankId == _client.OwnTankId:
                if (_ownWreck.Revived() == WreckTracker.Change.Restore) RestoreOwn();
                break;
            case GameEventType.TankDestroyed:
            case GameEventType.TankRevived:
                _actors.OnEvent(e);
                break;
        }
        _combat.OnShotEvent(e);
        _world!.OnEvent(e);
        PropFx.OnEvent(e, field, _fx, _world.Forests);
    }

    /// <summary>
    /// A shot the client didn't predict. The own tank's (after a hard resync the server's shell flies like anyone's)
    /// shows at its own barrel; another tank's where the event says, with the recoil if that tank is in sight.
    /// </summary>
    private void OnShellFired(in GameEvent e)
    {
        if (e.TankId == _client.OwnTankId && _tank is not null)
        {
            _tank.Kick(_client.Own.TurretYaw);
            _fx.Muzzle(_tank.MuzzleTransform, withSmoke: !_camera.Sniper);
            return;
        }
        _fx.Muzzle(MuzzleOf(e));
        if (e.TankId != 0) _actors.Kick(e.TankId);
    }

    /// <summary>The muzzle of a shot event: at its position, −Z along the shell's flight (upright when the flight is vertical or unknown).</summary>
    private static Transform3D MuzzleOf(in GameEvent e)
    {
        Vector3 at = e.Position.ToGodot();
        Vector3 along = e.Velocity.ToGodot();
        if (along.LengthSquared() < 1e-6f) return new Transform3D(Basis.Identity, at);
        along = along.Normalized();
        if (Mathf.Abs(along.Y) > 0.99f) return new Transform3D(Basis.Identity, at);
        return new Transform3D(Basis.LookingAt(along, Vector3.Up), at);
    }

    /// <summary>The own tank is back: the model intact and AP loaded (a respawned tank starts with AP; still asking for HE would cost a full reload).</summary>
    private void RestoreOwn()
    {
        _tank?.Restore();
        _input.ResetAmmo();
    }

    /// <summary>
    /// A kill for the feed (blueprint §3.3): names from the scoreboard's tank ids by room slot, sides from the teams as the
    /// scene colours tanks. A shooter the server hid (id 0), or one that has since left, gives a line without a killer.
    /// </summary>
    private void FeedKill(in GameEvent e)
    {
        PlayerStats[]? players = _client.Latest?.Players;
        if (players is null) return;
        byte ownTeam = OwnTeam(_client.Own);
        (string victim, Side victimSide) = Describe(players, e.TargetTankId, ownTeam);
        string? killer = null;
        Side killerSide = Side.Enemy;
        if (MatchHudFrame.SlotOf(players, e.TankId) >= 0) (killer, killerSide) = Describe(players, e.TankId, ownTeam);
        _hud.OnKill(killer, killerSide, victim, victimSide, killer is not null && KillFeed.IsFriendly(killerSide, victimSide), _time);
    }

    private (string Name, Side Side) Describe(PlayerStats[] players, int tankId, byte ownTeam)
    {
        int slot = MatchHudFrame.SlotOf(players, tankId);
        string name = slot >= 0 && _names[slot] is { } known ? known : UnknownName;
        byte team = slot >= 0 ? players[slot].Team : MatchRules.NoTeam;
        return (name, MatchColors.SideOf(Mode, ownTeam, team, isOwn: tankId == _client.OwnTankId));
    }
}
