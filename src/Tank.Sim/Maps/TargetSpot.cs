using System.Numerics;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Maps;

/// <summary>Where a practice target stands, ground position (X is world X, Y is world Z) and hull yaw, and what tank it is.</summary>
public readonly record struct TargetSpot(Vector2 Position, float Yaw, TankSpec Spec);
