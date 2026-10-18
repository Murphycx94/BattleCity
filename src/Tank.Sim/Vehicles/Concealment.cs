namespace Tank.Sim.Vehicles;

/// <summary>
/// How hard a tank is to spot (design §6), 0 … 1: the share of an observer's view range (beyond the first 15 m) it
/// takes away, while the tank is moving and while it stands still.
/// </summary>
public readonly record struct Concealment(float Moving, float Still);
