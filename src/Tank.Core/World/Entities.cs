using Tank.Core.Math;

namespace Tank.Core.World;

public enum TankKind : byte
{
    Player = 0,
    Basic = 1,
    Fast = 2,
    Power = 3,
    Armor = 4,
}

public enum PowerUpType : byte
{
    Helmet = 0,
    Clock = 1,
    Shovel = 2,
    Star = 3,
    Grenade = 4,
    ExtraLife = 5,
}

public struct TankState
{
    public int Id;
    public TankKind Kind;
    public byte Team;
    public byte Slot;          // owning player slot, Teams.NoSlot for AI
    public int X, Z;           // top-left, sub units
    public Dir Dir;
    public byte Hp;
    public byte Stars;
    public ushort ShieldTicks;
    public byte SpawnTicks;    // spawn animation: cannot move, fire or be hit
    public byte SlideTicks;    // ice momentum
    public byte FireCooldown;
    public bool Moving;
    public bool Blocked;       // movement was cut short last tick (AI uses this)
    public bool Bonus;         // drops a power-up when hit
    public bool Dead;
    public ushort AiTimer;

    public readonly Box Box => new(X, Z, Units.TankSize, Units.TankSize);
    public readonly int CenterX => X + Units.TankSize / 2;
    public readonly int CenterZ => Z + Units.TankSize / 2;
    public readonly bool IsPlayer => Kind == TankKind.Player;
}

public struct BulletState
{
    public int Id;
    public int OwnerTankId;
    public byte OwnerSlot;     // Teams.NoSlot for AI
    public byte Team;
    public int X, Z;           // center, sub units
    public Dir Dir;
    public byte Speed;         // sub units per tick
    public bool Power;         // can break steel, digs two cells deep
    public byte LagTicks;      // lag compensation: test tank hits against positions this many ticks ago
    public int SpawnInputTick; // client input tick that fired it (prediction pairing), -1 for AI
    public bool Dead;

    public readonly Box Box => Box.Centered(X, Z, Units.BulletSize);
}

public struct PowerUpState
{
    public int Id;
    public PowerUpType Type;
    public int X, Z;           // top-left of a 1-tile pickup area
    public ushort Ttl;
    public bool Dead;

    public readonly Box Box => new(X, Z, Units.Tile, Units.Tile);
}

public struct EagleState
{
    public int Id;
    public byte Team;
    public int X, Z;
    public bool Alive;

    public readonly Box Box => new(X, Z, Units.Tile, Units.Tile);
}

public struct PlayerState
{
    public bool Active;
    public byte Team;
    public byte Lives;
    public byte Stars;         // reset on death
    public bool Eliminated;    // co-op: out of lives
    public int Score;
    public int Kills;
    public int Deaths;
    public int TankId;         // 0 when no tank on the field
    public ushort RespawnTicks;
}
