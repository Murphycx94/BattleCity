using System.Numerics;
using Tank.Sim.Combat;

namespace Tank.Sim;

public enum GameEventType : byte
{
    ShellFired,
    ShellExploded,
    TankHit,
    SplashDamage,
    TankDestroyed,
    TankRevived,
    PropDamaged,
    PropDestroyed,
    CratePicked,
}

/// <summary>What a shell did to the armour it struck. <see cref="None"/> on events that aren't hits.</summary>
public enum HitResult : byte
{
    None,
    Penetrated,
    Ricochet,
    NotPenetrated,
}

/// <summary>
/// What knocked a tank out, on <see cref="GameEventType.TankDestroyed"/> events (<see cref="Shell"/> on every other event).
/// Values go on the wire in 2 bits: append only, at most 3.
/// </summary>
public enum KillCause : byte
{
    /// <summary>A shell (or its splash) from <see cref="GameEvent.TankId"/>.</summary>
    Shell = 0,

    /// <summary>The royale's zone (<see cref="Simulation.HurtByZone"/>): nobody's kill, <see cref="GameEvent.TankId"/> and <see cref="GameEvent.ShellId"/> 0.</summary>
    Zone = 1,
}

/// <summary>
/// Something presentation should react to. <see cref="Velocity"/> is zero when not meaningful.
/// A shell that leaves the map or times out emits no event; clients key any per-shell visuals by <c>Shell.Id</c> and retire them when it disappears.
/// <list type="bullet">
/// <item><see cref="GameEventType.ShellFired"/>: <see cref="TankId"/> fired <see cref="ShellId"/> from <see cref="Position"/> at <see cref="Velocity"/>.</item>
/// <item><see cref="GameEventType.ShellExploded"/>: an HE burst, or any shell striking the ground, a rock, a wall or a wreck, at <see cref="Position"/>.</item>
/// <item><see cref="GameEventType.TankHit"/>: a shell from <see cref="TankId"/> struck <see cref="TargetTankId"/> at <see cref="Position"/>;
/// <see cref="Velocity"/> holds the struck face's outward normal.</item>
/// <item><see cref="GameEventType.SplashDamage"/>: an HE burst at <see cref="Position"/> hurt <see cref="TargetTankId"/>.</item>
/// <item><see cref="GameEventType.TankDestroyed"/>: <see cref="TargetTankId"/> was knocked out by <see cref="TankId"/>; <see cref="Position"/> is the wreck.
/// <see cref="Cause"/> says how: by the zone, <see cref="TankId"/> and <see cref="ShellId"/> are 0.</item>
/// <item><see cref="GameEventType.TankRevived"/>: <see cref="TargetTankId"/> (also <see cref="TankId"/>) is back at full health.</item>
/// <item><see cref="GameEventType.PropDamaged"/>: shell <see cref="ShellId"/> from <see cref="TankId"/> did <see cref="Damage"/>
/// to wall <see cref="PropId"/> (the shell's damage, as on <see cref="GameEventType.TankHit"/>: the wall may have had fewer
/// hit points left). A direct hit: <see cref="Position"/> is where it struck and <see cref="Velocity"/> the shell's;
/// a splash: <see cref="Position"/> is the burst and <see cref="Velocity"/> zero.</item>
/// <item><see cref="GameEventType.PropDestroyed"/>: <see cref="TankId"/> destroyed prop <see cref="PropId"/> (a wall turned to rubble,
/// a fence knocked flat, a bush shot away) with shell <see cref="ShellId"/> (<see cref="Ammo"/> its round), or, when that is
/// 0, by driving into it. <see cref="Position"/> is where: the point the shell struck or entered it, the burst, or for a
/// tank the prop's centre on the ground. <see cref="Velocity"/> is the level unit direction it was pushed (the shell's
/// flight, outward from the burst, the hull's travel): a fence falls that way.</item>
/// <item><see cref="GameEventType.CratePicked"/>: <see cref="TankId"/> ate supply crate <see cref="PropId"/> (its id in the match's
/// set, not a prop) at <see cref="Position"/> (on the ground); <see cref="Damage"/> is what it did, the take's first number
/// (<c>CrateTake.Amount</c>: health restored, AP rounds added, or 1 for a layer; 0 when it did nothing). Never sent to clients.</item>
/// </list>
/// </summary>
public readonly record struct GameEvent(
    GameEventType Type, int Tick, int TankId, int ShellId, Vector3 Position, Vector3 Velocity,
    int TargetTankId = 0, HitResult Result = HitResult.None, int Damage = 0, AmmoType Ammo = AmmoType.AP, int PropId = 0,
    KillCause Cause = KillCause.Shell);
