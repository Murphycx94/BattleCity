using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Net.Battle;

public sealed record BattleServerOptions
{
    public string RoomName { get; init; } = "坦克 3D";
    public int Port { get; init; } = BattleProtocol.DefaultPort;
    public BattleMode Mode { get; init; } = BattleMode.TeamBattle;

    /// <summary>
    /// The room's first map. The library's default is the sandbox map (tests and the soak are built on it); the game's
    /// entry points pass the riverland.
    /// </summary>
    public MapDefinition Map { get; init; } = MapCatalog.Sandbox;

    /// <summary>
    /// The maps the host may switch the room to, found by <see cref="MapDefinition.Id"/> ignoring case. The server checks
    /// that <see cref="Map"/> is one of them, that no two ids differ only in case and that every id fits
    /// <see cref="BattleProtocol.MaxMapIdBytes"/> in UTF-8 (<see cref="ArgumentException"/> otherwise).
    /// </summary>
    public IReadOnlyList<MapDefinition> Maps { get; init; } = MapCatalog.All;

    /// <summary>
    /// Generate and encode maps on a worker thread (a real host: the tick thread must never stall on a generator, and the
    /// room can't start until the map is ready); false does it on the thread that makes the server or picks the map, so a
    /// test or the loopback host runs the same every time.
    /// </summary>
    public bool PrepareMapsInBackground { get; init; }

    /// <summary>Bots added when a match starts, filling the thinner team first.</summary>
    public int Bots { get; init; }

    /// <summary>How long the result stays up before everyone is back in the room (the mode rules' 8 s).</summary>
    public double ReturnToRoomSeconds { get; init; } = ModeRules.ResultsTicks / (double)Tank.Sim.SimConstants.TickRate;
    public double ReconnectGraceSeconds { get; init; } = 30;

    /// <summary>
    /// Session token of the player running a listen server. Whoever says hello with it is host, regardless of who
    /// connected first.
    /// </summary>
    public uint? OwnerToken { get; init; }

    /// <summary>Fixed seed for reproducible matches (tests, bots' choices); null picks a random one per match.</summary>
    public uint? Seed { get; init; }

    /// <summary>The longest <see cref="MatchSeconds"/> accepted: a day, which keeps the tick count far from overflow.</summary>
    public const double MaxMatchSeconds = 24 * 60 * 60;

    private readonly double? _matchSeconds;

    /// <summary>A match length other than the mode's (tests), positive and at most <see cref="MaxMatchSeconds"/>; null for the mode's own.</summary>
    public double? MatchSeconds
    {
        get => _matchSeconds;
        init => _matchSeconds = value is { } seconds && !(seconds > 0 && seconds <= MaxMatchSeconds)
            ? throw new ArgumentOutOfRangeException(nameof(value), value, $"A match lasts more than 0 and at most {MaxMatchSeconds} seconds.")
            : value;
    }
}
