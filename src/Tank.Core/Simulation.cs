using Tank.Core.Math;
using Tank.Core.Systems;
using Tank.Core.World;

namespace Tank.Core;

/// <summary>
/// The single entry point that advances a match by one fixed 60Hz tick.
/// Order inside a tick is fixed (see docs/03-netcode.md §6.3), all math is integer and all randomness
/// comes from the seeded <see cref="GameState.Rng"/>, so identical inputs always give identical states.
/// </summary>
public sealed class Simulation
{
    private readonly List<GameEvent> _events = new(64);
    private readonly Box[] _obstacleScratch = new Box[GameState.MaxTanks + GameState.MaxEagles];

    public Simulation(MatchConfig config)
    {
        State = new GameState(config);
        History = new TankHistory();
        SpawnSystem.InitializeMatch(this);
    }

    public GameState State { get; }
    public TankHistory History { get; }

    /// <summary>Events produced by the most recent <see cref="Step"/>.</summary>
    public IReadOnlyList<GameEvent> Events => _events;

    public void AddPlayer(int slot, byte team)
    {
        ValidateSlot(slot);
        State.Players[slot] = new PlayerState
        {
            Active = true,
            Team = team,
            Lives = (byte)State.Config.StartingLives,
        };
    }

    public void RemovePlayer(int slot)
    {
        ValidateSlot(slot);
        ref var player = ref State.Players[slot];
        if (player.TankId != 0)
        {
            int index = State.FindTankIndex(player.TankId);
            if (index >= 0) State.Tanks[index].Dead = true;
        }
        player = default;
        State.TankCount = Compactor.CompactTanks(State.Tanks, State.TankCount);
    }

    /// <summary>Advances to tick <c>State.Tick + 1</c>. <paramref name="playerInputs"/> is indexed by slot.</summary>
    public void Step(ReadOnlySpan<InputCmd> playerInputs)
    {
        _events.Clear();
        State.Map.ClearChanges();
        State.Tick++;
        if (State.Phase == MatchPhase.Ended) return;

        TimerSystem.Run(this);
        SpawnSystem.Run(this);
        TankSystem.Run(this, playerInputs);
        BulletSystem.Run(this);
        PowerUpSystem.Run(this);
        RulesSystem.Run(this);
        Compactor.Run(State);
        History.Record(State);
    }

    internal void Emit(GameEventType type, int x, int z, int a = 0, int b = 0) =>
        _events.Add(new GameEvent(type, State.Tick, x, z, a, b));

    /// <summary>Boxes of every live tank and eagle except <paramref name="selfTankId"/>.</summary>
    internal ReadOnlySpan<Box> ObstaclesExcept(int selfTankId)
    {
        int n = 0;
        for (int i = 0; i < State.TankCount; i++)
        {
            ref readonly var t = ref State.Tanks[i];
            if (!t.Dead && t.Id != selfTankId) _obstacleScratch[n++] = t.Box;
        }
        for (int i = 0; i < State.EagleCount; i++)
            if (State.Eagles[i].Alive) _obstacleScratch[n++] = State.Eagles[i].Box;
        return _obstacleScratch.AsSpan(0, n);
    }

    private static void ValidateSlot(int slot)
    {
        if ((uint)slot >= GameState.MaxPlayers)
            throw new ArgumentOutOfRangeException(nameof(slot), $"Slot must be in [0, {GameState.MaxPlayers}).");
    }
}
