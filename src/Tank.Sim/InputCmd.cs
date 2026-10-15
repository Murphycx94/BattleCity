using Tank.Sim.Combat;

namespace Tank.Sim;

/// <summary>
/// One tick of driver input. <see cref="Throttle"/> and <see cref="Steer"/> are -1, 0 or 1 (Steer +1 = turn right).
/// <see cref="Fire"/> is level-triggered: hold to fire whenever reloaded; don't edge-detect.
/// Aim is a world-space direction from the gun trunnion (yaw as in Geometry.Angles, pitch positive up).
/// <see cref="Ammo"/> is the round the driver wants loaded; changing it unloads the gun and starts a full reload.
/// </summary>
public readonly record struct InputCmd(int Tick, sbyte Throttle, sbyte Steer, float AimYaw, float AimPitch, bool Fire, AmmoType Ammo = AmmoType.AP);
