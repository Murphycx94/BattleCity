using Tank.Sim.Maps;

namespace Tank.Client.World;

/// <summary>
/// What the scenery needs beyond the battlefield itself: where the grass islands are and the seed of the ground's dirt
/// patches (for a field without ground kinds of its own), and which map it is (<see cref="MapId"/>, a
/// <see cref="MapCatalog"/> id; null when not known), whose forests mix their trees' kinds by region. Kept apart so a
/// match can take them from its own map instead of the sandbox's.
/// </summary>
public readonly record struct MapStyle(IReadOnlyList<System.Numerics.Vector2> GrassIslands, uint Seed, string? MapId = null)
{
    public static MapStyle Sandbox => new(SandboxMap.BushPatches, (uint)SandboxMap.Seed, SandboxMap.Id);

    /// <summary>The sandbox's grass islands and dirt seed (only a field without ground kinds uses them) on map <paramref name="mapId"/>.</summary>
    public static MapStyle For(string? mapId) => Sandbox with { MapId = mapId };
}
