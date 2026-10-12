using Tank.Core.World;

namespace Tank.Net.Protocol;

public struct PlayerInfo : IEquatable<PlayerInfo>
{
    public bool Active;
    public byte Team;
    public byte Lives;
    public bool Eliminated;
    public int Score;
    public int Kills;
    public int Deaths;
    public int TankId;

    public readonly bool Equals(PlayerInfo o) =>
        Active == o.Active && Team == o.Team && Lives == o.Lives && Eliminated == o.Eliminated &&
        Score == o.Score && Kills == o.Kills && Deaths == o.Deaths && TankId == o.TankId;

    public override readonly bool Equals(object? obj) => obj is PlayerInfo p && Equals(p);

    public override readonly int GetHashCode() => HashCode.Combine(Active, Team, Lives, Score, Kills, TankId);
}

/// <summary>World state at one server tick, as seen on the wire. Entities are sorted by id.</summary>
public sealed class Snapshot
{
    public const int MaxEntities = 256;

    public int Tick;
    public readonly NetEntity[] Entities = new NetEntity[MaxEntities];
    public int EntityCount;
    public MatchPhase Phase;
    public byte WinnerTeam = Teams.None;
    public int EnemiesRemaining;
    public int TicksRemaining;
    public uint MapChecksum;
    public readonly PlayerInfo[] Players = new PlayerInfo[GameState.MaxPlayers];

    public ReadOnlySpan<NetEntity> EntitySpan => Entities.AsSpan(0, EntityCount);

    public void CopyFrom(Snapshot other)
    {
        Tick = other.Tick;
        Array.Copy(other.Entities, Entities, other.EntityCount);
        EntityCount = other.EntityCount;
        Phase = other.Phase;
        WinnerTeam = other.WinnerTeam;
        EnemiesRemaining = other.EnemiesRemaining;
        TicksRemaining = other.TicksRemaining;
        MapChecksum = other.MapChecksum;
        Array.Copy(other.Players, Players, Players.Length);
    }

    public bool TryFind(int id, out NetEntity entity)
    {
        int lo = 0, hi = EntityCount - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int midId = Entities[mid].Id;
            if (midId == id)
            {
                entity = Entities[mid];
                return true;
            }
            if (midId < id) lo = mid + 1;
            else hi = mid - 1;
        }
        entity = default;
        return false;
    }
}

/// <summary>Per-recipient data sent alongside a snapshot (prediction and clock sync).</summary>
public struct ClientHeader
{
    public int LastProcessedInputTick;
    public int InputSlack;      // newest received input tick minus the tick it is simulated at, in ticks
    public byte OwnSlideTicks;
    public byte OwnSpawnTicks;
    public byte OwnFireCooldown;
}
