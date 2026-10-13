namespace Tank.Sim;

/// <summary>
/// One tick of driver input. <see cref="Throttle"/> and <see cref="Steer"/> are -1, 0 or 1 (Steer +1 = turn right).
/// Aim is a world-space direction from the gun trunnion (yaw as in Geometry.Angles, pitch positive up).
/// </summary>
public readonly record struct InputCmd(int Tick, sbyte Throttle, sbyte Steer, float AimYaw, float AimPitch, bool Fire);
