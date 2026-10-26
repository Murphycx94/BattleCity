using System.Numerics;
using Tank.Sim.Modes;

namespace Tank.Sim.Maps;

/// <summary>
/// One map a match can be played on (riverland design §4.4, stage 5 design §2): its id and display name, how to generate
/// it, where tanks start, where bots drive and where the teams' bases are. The map is generated at most once: the first
/// <see cref="NewBattlefield"/> or <see cref="Data"/> runs the generator and keeps its <see cref="MapData"/>, and the
/// battlefield built from that data, private and untouched; every battlefield handed out is a fresh copy of it.
/// </summary>
/// <remarks>
/// There is one map per definition, the quantised one: <see cref="NewBattlefield"/> is <c>Data.Build()</c>, bit for bit
/// what a client builds from the <see cref="Data"/> the server sends, so a server, a client and a local game all play on
/// the same ground. The generator's own unrounded output is only seen by calling <see cref="Create"/>.
/// </remarks>
public sealed class MapDefinition
{
    private readonly Lazy<MapData> _data;
    private readonly Lazy<Battlefield> _built;   // _data.Value.Build(): the quantised map, never changed

    /// <param name="id">The map's name in commands and messages ("sandbox", "riverland").</param>
    /// <param name="name">What players see ("试驾场地图", "河谷").</param>
    /// <param name="create">The generator: called at most once.</param>
    /// <param name="bases">The teams' bases for a standard battle (<see cref="TeamBase.Check"/>): none, or one per team.</param>
    public MapDefinition(string id, string name, Func<Battlefield> create, SpawnLayout spawns, IReadOnlyList<Vector2> waypoints,
        IReadOnlyList<TeamBase>? bases = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(spawns);
        ArgumentNullException.ThrowIfNull(waypoints);
        Id = id;
        Name = name;
        Create = create;
        Spawns = spawns;
        Waypoints = waypoints;
        Bases = TeamBase.Check(bases, nameof(bases));
        _data = new Lazy<MapData>(() => MapData.Of(create()), LazyThreadSafetyMode.ExecutionAndPublication);
        _built = new Lazy<Battlefield>(() => _data.Value.Build(), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>
    /// The generator itself, its output unrounded; <see cref="NewBattlefield"/> and <see cref="Data"/> call it only once
    /// between them and use its quantised <see cref="Data"/>.
    /// </summary>
    public Func<Battlefield> Create { get; }

    public SpawnLayout Spawns { get; }

    public IReadOnlyList<Vector2> Waypoints { get; }

    /// <summary>The teams' bases (stage 5 design §3.2): empty when the map has none, otherwise one for each team.</summary>
    public IReadOnlyList<TeamBase> Bases { get; }

    /// <summary>
    /// The generated map as data for the wire (<see cref="MapData.Of"/>: heights and water level in whole centimetres),
    /// built once; the first call may take a while.
    /// </summary>
    public MapData Data => _data.Value;

    /// <summary>Whether <see cref="Data"/> has been built.</summary>
    public bool IsDataReady => _data.IsValueCreated;

    /// <summary>
    /// A battlefield of this map with every prop standing at full hit points: <see cref="Battlefield.Fresh"/> of the one
    /// built from <see cref="Data"/> (shared and never changed), so bit for bit what <c>Data.Build()</c> gives anywhere;
    /// the first call generates the map.
    /// </summary>
    public Battlefield NewBattlefield() => _built.Value.Fresh();
}
