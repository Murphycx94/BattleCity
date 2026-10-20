using System.Globalization;
using System.Net;
using Tank.Net.Battle;
using Tank.Client.UI;
using Tank.Net.Transport;
using Tank.Sim.Modes;

namespace Tank.Client.Online;

/// <summary>
/// A battle room found on the LAN, ready for the menu's list: one line of text, and whether joining it can work.
/// <see cref="WhyNot"/> is set for a room that can't be joined, and also as a note for one that can but won't put the
/// player into the running match.
/// </summary>
public sealed record LanRoomRow(IPEndPoint EndPoint, string Text, bool Joinable, string? WhyNot)
{
    private const int NameColumns = 18, ModeColumns = 12, StateColumns = 7;

    public static LanRoomRow From(DiscoveredBattleRoom room)
    {
        RoomInfoMessage info = room.Info;
        bool versionOk = info.ProtocolVersion == BattleProtocol.Version;
        bool full = info.Players >= info.MaxPlayers;
        string state = !versionOk ? "版本不符" : info.MatchRunning ? "对战中" : "等待中";
        string text = string.Create(CultureInfo.InvariantCulture,
            $"{TextColumns.PadColumns(info.RoomName, NameColumns)} {TextColumns.PadColumns(ModeText.Name(info.Mode), ModeColumns)} " +
            $"{info.Players}/{info.MaxPlayers}  {TextColumns.PadColumns(state, StateColumns)} {room.PingMs,3}ms   {room.EndPoint.Address}");
        (bool joinable, string? why) = (versionOk, full) switch
        {
            (false, _) => (false, $"版本不符：房间 v{info.ProtocolVersion}，本机 v{BattleProtocol.Version}"),
            (_, true) => (false, "房间已满"),
            _ when info.MatchRunning && !ModeRules.JoinsMidMatch(info.Mode) => (true, "对局进行中：进入房间后等下一局"),
            _ => (true, (string?)null),
        };
        return new LanRoomRow(room.EndPoint, text, joinable, why);
    }
}
