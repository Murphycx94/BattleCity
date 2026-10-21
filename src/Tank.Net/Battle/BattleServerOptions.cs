using Tank.Sim.Modes;

namespace Tank.Net.Battle;

public sealed record BattleServerOptions
{
    public string RoomName { get; init; } = "坦克 3D";
    public int Port { get; init; } = BattleProtocol.DefaultPort;
    public BattleMode Mode { get; init; } = BattleMode.TeamBattle;

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
