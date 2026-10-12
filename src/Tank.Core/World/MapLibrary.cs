
namespace Tank.Core.World;

/// <summary>Built-in maps embedded in the assembly, so server and every client ship identical copies.</summary>
public static class MapLibrary
{
    private const string Prefix = "Tank.Core.Maps.";
    private const string Suffix = ".map";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Sources = new(LoadSources);

    public static IReadOnlyList<string> Names => Sources.Value.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    public static string GetSource(string name) =>
        Sources.Value.TryGetValue(name, out var text)
            ? text
            : throw new KeyNotFoundException($"Unknown map '{name}'. Available: {string.Join(", ", Names)}.");

    public static MapDefinition Load(string name) => MapParser.Parse(name, GetSource(name));

    /// <summary>Default map for a mode.</summary>
    public static string DefaultFor(GameMode mode) => mode switch
    {
        GameMode.Coop => "coop-01",
        GameMode.BaseBattle => "versus-01",
        _ => "arena-01",
    };

    private static IReadOnlyDictionary<string, string> LoadSources()
    {
        var asm = typeof(MapLibrary).Assembly;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resource in asm.GetManifestResourceNames())
        {
            if (!resource.StartsWith(Prefix, StringComparison.Ordinal) || !resource.EndsWith(Suffix, StringComparison.Ordinal))
                continue;
            using var stream = asm.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing embedded map resource '{resource}'.");
            using var reader = new StreamReader(stream);
            result[resource[Prefix.Length..^Suffix.Length]] = reader.ReadToEnd();
        }
        return result;
    }
}
