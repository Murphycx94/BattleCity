using System.Diagnostics.CodeAnalysis;

namespace Tank.Sim.Maps;

/// <summary>Every map a match can be played on, by id (riverland design §4.4; stage 5 design §2).</summary>
public static class MapCatalog
{
    public static MapDefinition Sandbox { get; } =
        new(SandboxMap.Id, "试驾场地图", SandboxMap.Create, SandboxMap.Spawns, SandboxMap.Waypoints, SandboxMap.Bases);

    public static MapDefinition Riverland { get; } =
        new(RiverlandMap.Id, "河谷", RiverlandMap.Create, RiverlandMap.Spawns, RiverlandMap.Waypoints, RiverlandMap.Bases);

    /// <summary>The sandbox first, then the riverland.</summary>
    public static IReadOnlyList<MapDefinition> All { get; } = new[] { Sandbox, Riverland };

    /// <summary>The map whose id is <paramref name="id"/>, ignoring case; false for null or an unknown id.</summary>
    public static bool TryGet(string? id, [MaybeNullWhen(false)] out MapDefinition map)
    {
        foreach (MapDefinition m in All)
            if (string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                map = m;
                return true;
            }
        map = null;
        return false;
    }

    /// <summary>The map whose id is <paramref name="id"/>, ignoring case.</summary>
    /// <exception cref="ArgumentException">No map has that id.</exception>
    public static MapDefinition Get(string id) =>
        TryGet(id, out MapDefinition? map) ? map : throw new ArgumentException($"No map \"{id}\".", nameof(id));
}
