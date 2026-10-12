using Tank.Core.Math;

namespace Tank.Core.World;

/// <summary>
/// Complete authoritative state of one match. Entity arrays are preallocated and kept sorted by id
/// (ids only grow, dead entities are compacted in order), so iteration order is deterministic.
/// </summary>
public sealed class GameState
{
    public const int MaxTanks = 40;
    public const int MaxBullets = 160;
    public const int MaxPowerUps = 8;
    public const int MaxEagles = 2;
    public const int MaxPlayers = 8;

    public GameState(MatchConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Map = config.Map.CreateMap();
        Rng = new XorShift(config.Seed);
    }

    public MatchConfig Config { get; }
    public GameMap Map { get; }
    public int Tick;
    public XorShift Rng;
    public int NextId = 1;

    public readonly TankState[] Tanks = new TankState[MaxTanks];
    public int TankCount;
    public readonly BulletState[] Bullets = new BulletState[MaxBullets];
    public int BulletCount;
    public readonly PowerUpState[] PowerUps = new PowerUpState[MaxPowerUps];
    public int PowerUpCount;
    public readonly EagleState[] Eagles = new EagleState[MaxEagles];
    public int EagleCount;
    public readonly PlayerState[] Players = new PlayerState[MaxPlayers];

    public MatchPhase Phase;
    public byte WinnerTeam = Teams.None;
    public int EnemiesToSpawn;
    public int EnemiesSpawned;
    public ushort EnemySpawnTimer;
    public int EnemySpawnCursor;
    public int TicksRemaining;
    public readonly ushort[] FreezeTicks = new ushort[Teams.Count];
    public readonly ushort[] ShovelTicks = new ushort[MaxEagles];

    public int AllocateId() => NextId++;

    public int FindTankIndex(int id)
    {
        for (int i = 0; i < TankCount; i++)
            if (Tanks[i].Id == id) return i;
        return -1;
    }

    public int AliveEnemyCount()
    {
        int n = 0;
        for (int i = 0; i < TankCount; i++)
            if (!Tanks[i].Dead && !Tanks[i].IsPlayer) n++;
        return n;
    }

    public int ActiveBulletsOf(int tankId)
    {
        int n = 0;
        for (int i = 0; i < BulletCount; i++)
            if (!Bullets[i].Dead && Bullets[i].OwnerTankId == tankId) n++;
        return n;
    }

    /// <summary>Hash of everything gameplay-relevant; equal hashes mean equal simulations.</summary>
    public uint ComputeHash()
    {
        uint h = Map.ComputeChecksum();
        h = Mix(h, (uint)Tick);
        h = Mix(h, Rng.State);
        for (int i = 0; i < TankCount; i++)
        {
            ref readonly var t = ref Tanks[i];
            h = Mix(h, (uint)t.Id);
            h = Mix(h, (uint)t.X);
            h = Mix(h, (uint)t.Z);
            h = Mix(h, (uint)t.Dir | ((uint)t.Hp << 8) | ((uint)t.Stars << 16));
        }
        for (int i = 0; i < BulletCount; i++)
        {
            ref readonly var b = ref Bullets[i];
            h = Mix(h, (uint)b.Id);
            h = Mix(h, (uint)b.X);
            h = Mix(h, (uint)b.Z);
        }
        for (int i = 0; i < MaxPlayers; i++) h = Mix(h, (uint)Players[i].Score);
        return h;
    }

    private static uint Mix(uint h, uint v)
    {
        h ^= v;
        h *= 16777619;
        return h ^ (h >> 15);
    }
}
