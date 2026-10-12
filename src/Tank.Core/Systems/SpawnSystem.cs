using Tank.Core.Math;
using Tank.Core.Rules;
using Tank.Core.World;

namespace Tank.Core.Systems;

internal static class SpawnSystem
{
    public static void InitializeMatch(Simulation sim)
    {
        var s = sim.State;
        var config = s.Config;
        if (config.Mode != GameMode.Deathmatch)
        {
            foreach (var e in config.Map.Eagles)
            {
                if (config.Mode == GameMode.Coop && e.Team != 0) continue;
                if (s.EagleCount >= GameState.MaxEagles) break;
                s.Eagles[s.EagleCount++] = new EagleState { Id = s.AllocateId(), Team = e.Team, X = e.X, Z = e.Z, Alive = true };
            }
        }
        if (config.Mode == GameMode.Coop)
        {
            s.EnemiesToSpawn = config.Map.EnemySpawns.Count > 0 ? config.EnemyTotal : 0;
            s.EnemySpawnTimer = 60;
        }
        if (config.Mode == GameMode.Deathmatch) s.TicksRemaining = config.DeathmatchTimeLimitTicks;
    }

    public static void Run(Simulation sim)
    {
        var s = sim.State;
        for (int slot = 0; slot < GameState.MaxPlayers; slot++)
        {
            ref var p = ref s.Players[slot];
            if (!p.Active || p.Eliminated || p.TankId != 0) continue;
            if (p.RespawnTicks > 0)
            {
                p.RespawnTicks--;
                continue;
            }
            TrySpawnPlayer(sim, slot);
        }
        if (s.Config.Mode == GameMode.Coop) SpawnEnemies(sim);
    }

    private static void TrySpawnPlayer(Simulation sim, int slot)
    {
        var s = sim.State;
        ref var p = ref s.Players[slot];
        var spawns = s.Config.Map.PlayerSpawns;
        int count = spawns.Count;
        for (int k = 0; k < count; k++)
        {
            var sp = spawns[(slot + k) % count];
            bool teamMatches = s.Config.Mode switch
            {
                GameMode.BaseBattle => sp.Team == p.Team,
                GameMode.Coop => sp.Team == 0,
                _ => true,
            };
            if (!teamMatches || !IsFree(s, sp.X, sp.Z)) continue;

            int id = AddTank(s, new TankState
            {
                Kind = TankKind.Player,
                Team = p.Team,
                Slot = (byte)slot,
                X = sp.X,
                Z = sp.Z,
                Dir = sp.Team == 1 ? Dir.Down : Dir.Up,
                Hp = 1,
                Stars = p.Stars,
                ShieldTicks = TankStats.SpawnShieldTicks,
                SpawnTicks = TankStats.PlayerSpawnTicks,
            });
            if (id == 0) return;
            p.TankId = id;
            sim.Emit(GameEventType.TankSpawned, sp.X + Units.Tile / 2, sp.Z + Units.Tile / 2, (int)TankKind.Player, slot);
            return;
        }
    }

    private static void SpawnEnemies(Simulation sim)
    {
        var s = sim.State;
        var spawns = s.Config.Map.EnemySpawns;
        if (s.EnemiesToSpawn <= 0 || spawns.Count == 0) return;
        if (s.EnemySpawnTimer > 0)
        {
            s.EnemySpawnTimer--;
            return;
        }
        int activePlayers = 0;
        for (int i = 0; i < GameState.MaxPlayers; i++) if (s.Players[i].Active) activePlayers++;
        if (s.AliveEnemyCount() >= TankStats.MaxEnemiesOnField(activePlayers)) return;

        var sp = spawns[s.EnemySpawnCursor % spawns.Count];
        if (!IsFree(s, sp.X, sp.Z))
        {
            s.EnemySpawnCursor++;
            return;
        }

        var roster = EnemyRoster.ForStage(s.Config.Stage, s.Config.EnemyTotal);
        var kind = roster[s.EnemiesSpawned % roster.Length];
        int id = AddTank(s, new TankState
        {
            Kind = kind,
            Team = Teams.Enemy,
            Slot = Teams.NoSlot,
            X = sp.X,
            Z = sp.Z,
            Dir = Dir.Down,
            Hp = TankStats.InitialHp(kind),
            SpawnTicks = TankStats.EnemySpawnTicks,
            Bonus = EnemyRoster.IsBonus(s.EnemiesSpawned),
        });
        if (id == 0) return;
        sim.Emit(GameEventType.TankSpawned, sp.X + Units.Tile / 2, sp.Z + Units.Tile / 2, (int)kind, Teams.NoSlot);
        s.EnemiesSpawned++;
        s.EnemiesToSpawn--;
        s.EnemySpawnCursor++;
        s.EnemySpawnTimer = TankStats.EnemySpawnIntervalTicks;
    }

    private static int AddTank(GameState s, TankState tank)
    {
        if (s.TankCount >= GameState.MaxTanks) return 0;
        tank.Id = s.AllocateId();
        s.Tanks[s.TankCount++] = tank;
        return tank.Id;
    }

    private static bool IsFree(GameState s, int x, int z)
    {
        var box = new Box(x, z, Units.TankSize, Units.TankSize);
        for (int i = 0; i < s.TankCount; i++)
            if (!s.Tanks[i].Dead && s.Tanks[i].Box.Overlaps(box)) return false;
        return true;
    }
}
