using Tank.Core.World;

namespace Tank.Net.Protocol;

/// <summary>Projects the authoritative <see cref="GameState"/> into a wire <see cref="Snapshot"/>.</summary>
public static class SnapshotBuilder
{
    private const int ExpiringTicks = 180;

    private static readonly IComparer<NetEntity> ById = Comparer<NetEntity>.Create(static (a, b) => a.Id.CompareTo(b.Id));

    public static void Build(GameState s, Snapshot output)
    {
        output.Tick = s.Tick;
        int n = 0;
        for (int i = 0; i < s.TankCount; i++)
        {
            ref readonly var t = ref s.Tanks[i];
            if (t.Dead) continue;
            var flags = EntityFlags.None;
            if (t.ShieldTicks > 0) flags |= EntityFlags.Shield;
            if (t.SpawnTicks > 0) flags |= EntityFlags.Spawning;
            if (t.Moving) flags |= EntityFlags.Moving;
            if (t.Bonus) flags |= EntityFlags.Bonus;
            if (s.FreezeTicks[t.Team] > 0) flags |= EntityFlags.Frozen;
            output.Entities[n++] = new NetEntity
            {
                Id = t.Id, Kind = EntityKind.Tank, Type = (byte)t.Kind, Team = t.Team, Slot = t.Slot,
                X = t.X, Z = t.Z, Dir = (byte)t.Dir, Hp = t.Hp, Flags = flags, Aux = t.Stars,
            };
        }
        for (int i = 0; i < s.BulletCount && n < Snapshot.MaxEntities; i++)
        {
            ref readonly var b = ref s.Bullets[i];
            if (b.Dead) continue;
            output.Entities[n++] = new NetEntity
            {
                Id = b.Id, Kind = EntityKind.Bullet, Type = b.Speed, Team = b.Team, Slot = b.OwnerSlot,
                X = b.X, Z = b.Z, Dir = (byte)b.Dir, Flags = b.Power ? EntityFlags.Power : EntityFlags.None,
                Aux = b.SpawnInputTick,
            };
        }
        for (int i = 0; i < s.PowerUpCount && n < Snapshot.MaxEntities; i++)
        {
            ref readonly var p = ref s.PowerUps[i];
            if (p.Dead) continue;
            output.Entities[n++] = new NetEntity
            {
                Id = p.Id, Kind = EntityKind.PowerUp, Type = (byte)p.Type, Team = Teams.None, Slot = Teams.NoSlot,
                X = p.X, Z = p.Z, Flags = p.Ttl < ExpiringTicks ? EntityFlags.Expiring : EntityFlags.None,
            };
        }
        for (int i = 0; i < s.EagleCount && n < Snapshot.MaxEntities; i++)
        {
            ref readonly var e = ref s.Eagles[i];
            output.Entities[n++] = new NetEntity
            {
                Id = e.Id, Kind = EntityKind.Eagle, Team = e.Team, Slot = Teams.NoSlot,
                X = e.X, Z = e.Z, Flags = e.Alive ? EntityFlags.Alive : EntityFlags.None,
            };
        }
        output.EntityCount = n;
        Array.Sort(output.Entities, 0, n, ById);

        output.Phase = s.Phase;
        output.WinnerTeam = s.WinnerTeam;
        output.EnemiesRemaining = s.EnemiesToSpawn + s.AliveEnemyCount();
        output.TicksRemaining = s.TicksRemaining;
        output.MapChecksum = s.Map.ComputeChecksum();
        for (int i = 0; i < GameState.MaxPlayers; i++)
        {
            ref readonly var p = ref s.Players[i];
            output.Players[i] = new PlayerInfo
            {
                Active = p.Active, Team = p.Team, Lives = p.Lives, Eliminated = p.Eliminated,
                Score = p.Score, Kills = p.Kills, Deaths = p.Deaths, TankId = p.TankId,
            };
        }
    }
}
