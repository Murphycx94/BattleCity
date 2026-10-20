using Tank.Sim.Maps;

namespace Tank.Client.World;

/// <summary>
/// What the scenery needs beyond the battlefield itself: where the grass islands are and the seed of the ground's dirt
/// patches. Kept apart so a match can take them from its own map instead of the sandbox's.
/// </summary>
public readonly record struct MapStyle(IReadOnlyList<System.Numerics.Vector2> GrassIslands, uint Seed)
{
    public static MapStyle Sandbox => new(SandboxMap.BushPatches, (uint)SandboxMap.Seed);
}
