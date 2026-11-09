using Tank.Sim.Maps;

namespace Tank.Client.UI;

/// <summary>The words for the maps, and the window titles that name them (riverland design §6.1, stage 5 design §2).</summary>
public static class MapText
{
    public const string AppTitle = "坦克 3D";
    private const string TestDrive = "试驾场";
    private const string Match = "对局";
    private const string NoMap = "—";
    private const string Separator = " · ";

    /// <summary>The catalog's name for <paramref name="mapId"/> (ignoring case); an unknown id as it is; "—" for none.</summary>
    public static string Name(string? mapId)
    {
        if (string.IsNullOrEmpty(mapId)) return NoMap;
        return MapCatalog.TryGet(mapId, out MapDefinition? map) ? map.Name : mapId;
    }

    /// <summary>The test drive's window title: "坦克 3D · 试驾场" on the sandbox map (as it always was), else with the map's name after it.</summary>
    public static string SandboxTitle(string mapId)
    {
        string title = AppTitle + Separator + TestDrive;
        return MapCatalog.TryGet(mapId, out MapDefinition? map) && map != MapCatalog.Sandbox ? title + Separator + map.Name : title;
    }

    /// <summary>A match's window title, with the map's name; "坦克 3D · 对局" when the map isn't known.</summary>
    public static string MatchTitle(string? mapId) =>
        AppTitle + Separator + (MapCatalog.TryGet(mapId, out MapDefinition? map) ? map.Name : Match);
}
