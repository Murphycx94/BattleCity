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
/// Something presentation should react to. <see cref="Velocity"/> is zero when not meaningful.
/// A shell that leaves the map or times out emits no event; clients key any per-shell visuals by <c>Shell.Id</c> and retire them when it disappears.
/// <list type="bullet">
/// <item><see cref="GameEventType.ShellFired"/>: <see cref="TankId"/> fired <see cref="ShellId"/> from <see cref="Position"/> at <see cref="Velocity"/>.</item>
/// <item><see cref="GameEventType.ShellExploded"/>: an HE burst, or any shell striking the ground, a rock or a wreck, at <see cref="Position"/>.</item>
/// <item><see cref="GameEventType.TankHit"/>: a shell from <see cref="TankId"/> struck <see cref="TargetTankId"/> at <see cref="Position"/>;
/// <see cref="Velocity"/> holds the struck face's outward normal.</item>
/// <item><see cref="GameEventType.SplashDamage"/>: an HE burst at <see cref="Position"/> hurt <see cref="TargetTankId"/>.</item>
/// <item><see cref="GameEventType.TankDestroyed"/>: <see cref="TargetTankId"/> was knocked out by <see cref="TankId"/>; <see cref="Position"/> is the wreck.</item>
/// <item><see cref="GameEventType.TankRevived"/>: <see cref="TargetTankId"/> (also <see cref="TankId"/>) is back at full health.</item>
/// </list>
/// </summary>
public readonly record struct GameEvent(
    GameEventType Type, int Tick, int TankId, int ShellId, Vector3 Position, Vector3 Velocity,
    int TargetTankId = 0, HitResult Result = HitResult.None, int Damage = 0, AmmoType Ammo = AmmoType.AP);
