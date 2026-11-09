using Tank.Net.Battle;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using BitReader = Tank.Net.Protocol.BitReader;
using BitWriter = Tank.Net.Protocol.BitWriter;
using ProtocolException = Tank.Net.Protocol.ProtocolException;

namespace Tank.Net.Tests.Battle;

/// <summary>
/// Protocol v7 (stage 6 design §5): duos on the wire. Not a bit of the layout changes — the mode byte takes 5 (6 and up
/// are refused), a room roster's 4-bit team carries 0–7, and the match block and the scoreboard carry a duos match's
/// winning team, its team count as the entrants and each seat's team place as its rank.
/// </summary>
public sealed class DuoWireTests
{
    [Fact]
    public void The_protocol_is_version_7()
    {
        Assert.Equal(7, BattleProtocol.Version);
        Assert.Equal(BattleMode.Duos, ModeRules.LastMode);
        Assert.Equal(5, (byte)BattleMode.Duos);
    }

    [Fact]
    public void A_duos_room_with_teams_up_to_7_round_trips()
    {
        var players = new[]
        {
            new RoomPlayer(0, "安娜", 0, 1, true, true, false),
            new RoomPlayer(1, "Ben", 0, 2, false, true, false),
            new RoomPlayer(2, "Cleo", 7, 3, true, false, false),
            new RoomPlayer(3, "机器人 1", 7, 0, true, true, true),
        };
        var room = new RoomStateMessage(BattleMode.Duos, 0, 15, false, players, RiverlandMap.Id, MapState.Generating);

        RoomStateMessage back = ReadRoomState(Encode(w => BattleMessages.Write(w, room)));

        Assert.Equal((BattleMode.Duos, (byte)0, (byte)15, false, RiverlandMap.Id, MapState.Generating),
            (back.Mode, back.HostSlot, back.Bots, back.MatchRunning, back.Map, back.MapState));
        Assert.Equal(players, back.Players);
        Assert.Equal(new byte[] { 0, 0, 7, 7 }, back.Players.Select(p => p.Team));
    }

    [Fact]
    public void A_duos_match_start_round_trips()
    {
        var start = new MatchStartMessage(BattleMode.Duos, 4, 150, 18150, null, Array.Empty<PropState>(), 9, null,
            new ZoneSync(150, new[] { new ZoneRing(new System.Numerics.Vector2(600f, 600f), 806f), new ZoneRing(new System.Numerics.Vector2(560f, 640f), 420f) }));

        MatchStartMessage back = ReadMatchStart(Encode(w => BattleMessages.Write(w, start)));

        Assert.Equal((BattleMode.Duos, 4, 150, 18150, 9), (back.Mode, back.MatchNumber, back.CurrentTick, back.EndTick, back.MyTankId));
        Assert.Equal(start.Zone!.Rings, back.Zone!.Rings);
    }

    [Fact]
    public void Mode_6_is_refused_in_the_room_the_match_start_and_a_lan_answer_of_this_version()
    {
        var room = new RoomStateMessage((BattleMode)6, 0, 0, false, Array.Empty<RoomPlayer>(), RiverlandMap.Id);
        Assert.Contains("mode", Assert.Throws<ProtocolException>(() => ReadRoomState(Encode(w => BattleMessages.Write(w, room)))).Message);

        var start = new MatchStartMessage((BattleMode)6, 1, 5, 900, null, Array.Empty<PropState>(), 2);
        Assert.Contains("mode", Assert.Throws<ProtocolException>(() => ReadMatchStart(Encode(w => BattleMessages.Write(w, start)))).Message);

        var info = new RoomInfoMessage(BattleProtocol.Version, "双人房", BattleMode.Duos, 3, 16, false, 27015, RiverlandMap.Id);
        Assert.Equal(info, ReadRoomInfo(Encode(w => BattleMessages.Write(w, info))));
        Assert.Throws<ProtocolException>(() => ReadRoomInfo(Encode(w => BattleMessages.Write(w, info with { Mode = (BattleMode)6 }))));
        // A room of another version keeps whatever mode it named, so the list still shows it.
        Assert.Equal(6, (byte)ReadRoomInfo(Encode(w => BattleMessages.Write(w, info with { ProtocolVersion = 6, Mode = (BattleMode)6 }))).Mode);
    }

    [Fact]
    public void A_finished_duos_match_carries_the_winning_team_the_team_count_and_shared_team_places()
    {
        var s = new BattleSnapshot { Tick = 9000 };
        s.Match = new MatchBlock
        {
            Phase = MatchPhase.Ended, TicksLeft = 9000, WinnerTeam = 7, WinnerSlot = MatchRules.NoSlot,
            EndReason = MatchEndReason.Eliminated, Entrants = ModeRules.DuoTeams,
        };
        for (int p = 0; p < BattleProtocol.MaxPlayers; p++)
        {
            byte team = (byte)(p / ModeRules.DuoTeamSize);
            s.Players[p] = new PlayerStats
            {
                Active = true, Team = team, Spec = (byte)(p % 4), TankId = 100 + p, Kills = p % 3, Deaths = team == 7 ? 0 : 1,
                Alive = team == 7, Rank = (byte)(ModeRules.DuoTeams - team), Damage = p * 250, Lasted = 60 + p,
            };
        }

        var w = new BitWriter();
        new BattleSnapshotCodec().Write(w, s, null, default);
        var r = new BitReader(w.Data.ToArray());
        Assert.Equal(BattleMessage.Snapshot, (BattleMessage)r.ReadByte());
        var back = new BattleSnapshot();
        new BattleSnapshotCodec().Read(ref r, _ => null, back, out _, out _);

        Assert.Equal(s.Match, back.Match);
        Assert.Equal(((byte)7, MatchRules.NoSlot, (byte)8), (back.Match.WinnerTeam, back.Match.WinnerSlot, back.Match.Entrants));
        Assert.Equal(s.Players, back.Players);
        Assert.Equal(back.Players[14].Rank, back.Players[15].Rank);   // both of team 7: first
        Assert.Equal(1, back.Players[15].Rank);
        Assert.Equal(8, back.Players[0].Rank);
    }

    // ---- helpers ----

    private static byte[] Encode(Action<BitWriter> write)
    {
        var w = new BitWriter();
        write(w);
        return w.Data.ToArray();
    }

    private static BitReader Reader(byte[] packet, BattleMessage expected)
    {
        var r = new BitReader(packet);
        Assert.Equal(expected, (BattleMessage)r.ReadByte());
        return r;
    }

    private static RoomStateMessage ReadRoomState(byte[] packet)
    {
        BitReader r = Reader(packet, BattleMessage.RoomState);
        return BattleMessages.ReadRoomState(ref r);
    }

    private static MatchStartMessage ReadMatchStart(byte[] packet)
    {
        BitReader r = Reader(packet, BattleMessage.MatchStart);
        return BattleMessages.ReadMatchStart(ref r);
    }

    private static RoomInfoMessage ReadRoomInfo(byte[] packet)
    {
        BitReader r = Reader(packet, BattleMessage.RoomInfo);
        return BattleMessages.ReadRoomInfo(ref r);
    }
}
