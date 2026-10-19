using System.Numerics;

namespace Tank.Sim.Modes;

/// <summary>Where a tank may appear: a spot, the way it faces, and (for team spawns) whose it is.</summary>
public readonly record struct SpawnPoint(Vector2 Position, float Yaw, byte Team);

/// <summary>
/// A map's spawn points (stage 4 design §2.3): <see cref="Team"/> for the two-team modes (each team's own, facing the
/// other), <see cref="Solo"/> spread over the map for a free-for-all.
/// </summary>
public sealed record SpawnLayout(IReadOnlyList<SpawnPoint> Team, IReadOnlyList<SpawnPoint> Solo);
