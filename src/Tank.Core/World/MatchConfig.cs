namespace Tank.Core.World;

public enum GameMode : byte
{
    Coop = 0,
    BaseBattle = 1,
    Deathmatch = 2,
}

public enum MatchPhase : byte
{
    Playing = 0,
    Ended = 1,
}

public sealed record MatchConfig(
    GameMode Mode,
    MapDefinition Map,
    uint Seed,
    int Stage = 1,
    int EnemyTotal = 20,
    int StartingLives = 3,
    int DeathmatchScoreLimit = 15,
    int DeathmatchTimeLimitTicks = 5 * 60 * Units.TickRate);
