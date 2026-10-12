using Tank.Core.World;

namespace Tank.Core.Rules;

/// <summary>All tuning numbers, in sub units per tick at 60Hz.</summary>
public static class TankStats
{
    public const int SlowBullet = 32;   // 2 px/tick
    public const int FastBullet = 64;   // 4 px/tick
    public const int FireCooldownTicks = 8;
    public const int IceSlideTicks = 20;
    public const int PlayerSpawnTicks = 30;
    public const int EnemySpawnTicks = 60;
    public const int SpawnShieldTicks = 180;
    public const int HelmetShieldTicks = 600;
    public const int RespawnDelayTicks = 120;
    public const int CoopFreezeTicks = 600;
    public const int VersusFreezeTicks = 300;
    public const int ShovelTicks = 1200;
    public const int PowerUpTtlTicks = 900;
    public const int EnemySpawnIntervalTicks = 150;
    public const int MaxStars = 3;

    public static int Speed(in TankState t) => t.Kind switch
    {
        TankKind.Player => 12,
        TankKind.Basic => 8,
        TankKind.Fast => 20,
        TankKind.Power => 12,
        TankKind.Armor => 8,
        _ => 8,
    };

    public static int BulletSpeed(in TankState t) => t.Kind switch
    {
        TankKind.Player => t.Stars >= 1 ? FastBullet : SlowBullet,
        TankKind.Power => FastBullet,
        _ => SlowBullet,
    };

    public static int MaxBullets(in TankState t) => t.IsPlayer && t.Stars >= 2 ? 2 : 1;

    public static bool PowerBullet(in TankState t) => t.IsPlayer && t.Stars >= 3;

    public static byte InitialHp(TankKind kind) => kind == TankKind.Armor ? (byte)4 : (byte)1;

    public static int KillScore(TankKind kind) => kind switch
    {
        TankKind.Basic => 100,
        TankKind.Fast => 200,
        TankKind.Power => 300,
        TankKind.Armor => 400,
        _ => 1,
    };

    public static int MaxEnemiesOnField(int activePlayers) => System.Math.Min(4 + System.Math.Max(activePlayers, 1), 8);
}
