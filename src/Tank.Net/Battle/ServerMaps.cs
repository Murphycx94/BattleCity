using System.Collections.Concurrent;
using System.Diagnostics;
using Tank.Net.Protocol;
using Tank.Sim.Maps;

namespace Tank.Net.Battle;

/// <summary>
/// A map ready to play: its definition, its data and that data encoded for the wire (<see cref="BattleMessages.WriteMap"/>).
/// A class, not a record: value equality over a <c>byte[]</c> would only compare references, and nothing compares these.
/// </summary>
internal sealed class PreparedMap
{
    public PreparedMap(MapDefinition definition, MapData data, byte[] encodedMap)
    {
        Definition = definition;
        Data = data;
        EncodedMap = encodedMap;
    }

    public MapDefinition Definition { get; }

    public MapData Data { get; }

    /// <summary><see cref="Data"/> as <see cref="BattleMessages.WriteMap"/> writes it, whole bytes: copied into every match start.</summary>
    public byte[] EncodedMap { get; }
}

/// <summary>
/// The maps a server has generated and encoded (riverland design §5.2, stage 5 design §2), with the bots' navigation
/// grid built: each at most once, so every match start copies the same bytes. A real host prepares them on a worker, so
/// the tick thread never generates a map or builds a grid and only looks them up; tests and the loopback host prepare
/// them on the calling thread, which keeps a run deterministic.
/// The generated <see cref="MapData"/> and the battlefield matches start from (<see cref="MapDefinition.NewBattlefield"/>)
/// are the definition's own, shared by every server in the process; the encoding is this server's.
/// </summary>
internal sealed class ServerMaps
{
    /// <summary>Room for the sandbox map's 53 KB; the writer grows for a bigger one.</summary>
    private const int EncodeWriterBytes = 128 * 1024;

    private readonly bool _background;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<MapDefinition, PreparedMap> _ready = new();
    private readonly ConcurrentDictionary<MapDefinition, bool> _failed = new();
    private readonly ConcurrentDictionary<MapDefinition, bool> _started = new();   // under way, done or failed: never prepared twice
    private int _encodings;

    /// <param name="background">Prepare on a worker (<see cref="Task.Run(Action)"/>) instead of on the calling thread.</param>
    /// <param name="log">Hears <c>map &lt;id&gt; ready in &lt;ms&gt; ms</c> and <c>map &lt;id&gt; failed: …</c>, from the worker in the background.</param>
    public ServerMaps(bool background, Action<string> log)
    {
        _background = background;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>How many maps were encoded: one per map, however many matches and seats used it.</summary>
    public int Encodings => Volatile.Read(ref _encodings);

    /// <summary>
    /// Generates <paramref name="map"/> (unless the process already has), builds the battlefield matches start from and
    /// the bots' navigation grid, and encodes the map, on a worker or right here; nothing if it is prepared or under way.
    /// A generator, or a navigation grid build, that throws is a bug: it is logged and the map is
    /// <see cref="MapState.Failed"/> for good (no retry).
    /// Nothing is thrown to the caller in either mode, so a room command that picks a broken map can't leave the tick
    /// half done.
    /// </summary>
    public void Prepare(MapDefinition map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!_started.TryAdd(map, true)) return;
        if (_background) Task.Run(() => Build(map));
        else Build(map);
    }

    public MapState StateOf(MapDefinition map) =>
        _ready.ContainsKey(map) ? MapState.Ready : _failed.ContainsKey(map) ? MapState.Failed : MapState.Generating;

    public bool IsReady(MapDefinition map) => _ready.ContainsKey(map);

    /// <summary>Whether generating or encoding <paramref name="map"/> threw: it will never be ready.</summary>
    public bool IsFailed(MapDefinition map) => _failed.ContainsKey(map);

    /// <exception cref="InvalidOperationException"><paramref name="map"/> isn't ready.</exception>
    public PreparedMap Get(MapDefinition map) =>
        _ready.TryGetValue(map, out PreparedMap? prepared) ? prepared : throw new InvalidOperationException($"Map {map.Id} isn't ready.");

    private void Build(MapDefinition map)
    {
        var clock = Stopwatch.StartNew();
        PreparedMap prepared;
        try
        {
            prepared = Encode(map);
        }
        catch (Exception ex)
        {
            _failed[map] = true;   // before the log: a log that throws must not leave the map "generating" for good
            _log($"map {map.Id} failed: {ex}");
            return;
        }
        Interlocked.Increment(ref _encodings);
        _ready[map] = prepared;
        _log($"map {map.Id} ready in {clock.ElapsedMilliseconds} ms");   // after the try: a log that throws can't fail a ready map
    }

    private static PreparedMap Encode(MapDefinition map)
    {
        MapData data = map.Data;
        // The quantised battlefield, built once and kept by the definition: a match start takes a Fresh() copy of it (about
        // 0.1 ms, bit for bit Data.Build()) instead of building the map from its data on the tick thread. Likewise the bots'
        // navigation grid (about 20–40 ms on the riverland in Release), built from that battlefield: built here once (building
        // the battlefield first), a match with bots only copies it. A grid that fails to build fails the map, as a
        // generator that throws does.
        map.EnsureNavGrid();
        var writer = new BitWriter(EncodeWriterBytes);
        BattleMessages.WriteMap(writer, data);
        if (writer.BitLength % 8 != 0)   // copied in with WriteAligned: what follows it in a match start must start on a byte
            throw new InvalidOperationException($"The encoded map {map.Id} ends at bit {writer.BitLength}, not on a byte boundary.");
        return new PreparedMap(map, data, writer.Data.ToArray());
    }
}
