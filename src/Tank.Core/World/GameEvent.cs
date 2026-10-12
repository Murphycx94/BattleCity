namespace Tank.Core.World;

public enum GameEventType : byte
{
    BulletSpark = 0,      // bullet stopped by wall/steel/edge/another bullet; A = dir, B = owner slot
    TankHit = 1,          // armor absorbed a hit
    TankDestroyed = 2,    // A = victim tank kind, B = killer slot
    EagleDestroyed = 3,   // A = team
    PowerUpSpawned = 4,   // A = power-up type
    PowerUpTaken = 5,     // A = power-up type, B = slot
    TankSpawned = 6,      // A = tank kind
    Fired = 7,            // A = owner slot
    MatchEnded = 8,       // A = winner team
    ShieldBlocked = 9,
}

/// <summary>Something presentational that happened during a tick. Positions are sub units.</summary>
public readonly record struct GameEvent(GameEventType Type, int Tick, int X, int Z, int A = 0, int B = 0);
