using Tank.Sim;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Client.UI;

/// <summary>The words for the modes and the tanks as the menu, the room and the LAN list show them.</summary>
public static class ModeText
{
    private const int SecondsPerMinute = 60;

    /// <summary>The mode's name for people.</summary>
    public static string Name(BattleMode mode) => mode switch
    {
        BattleMode.TeamBattle => "团队对战",
        BattleMode.Annihilation => "团队歼灭",
        BattleMode.FreeForAll => "各自为战",
        _ => mode.ToString(),
    };

    /// <summary>One line on how the mode is won (the numbers come from <see cref="ModeRules"/>).</summary>
    public static string Rule(BattleMode mode)
    {
        int minutes = ModeRules.DurationTicks(mode) / (SimConstants.TickRate * SecondsPerMinute);
        return mode switch
        {
            BattleMode.TeamBattle => $"两队对抗，被击毁的坦克 {ModeRules.RespawnTicks / SimConstants.TickRate} 秒后复活；{minutes} 分钟内击毁多的一队获胜。",
            BattleMode.Annihilation => $"两队对抗，被击毁就出局；全歼对方获胜，{minutes} 分钟内没分出胜负算平局。",
            BattleMode.FreeForAll => $"人人为敌，被击毁就出局；最后留下的一辆获胜，{minutes} 分钟后按剩余血量定胜负。",
            _ => string.Empty,
        };
    }

    /// <summary>The tank's name by its place in <see cref="TankCatalog.All"/> (what the room messages carry).</summary>
    public static string TankName(int tankIndex) =>
        tankIndex >= 0 && tankIndex < TankCatalog.All.Count ? TankCatalog.All[tankIndex].Name : "未知车型";
}
