using Tank.Core.World;
using Tank.Net.Protocol;

namespace Tank.Net.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void BitWriter_RoundTripsMixedFields()
    {
        var w = new BitWriter(4);
        w.WriteBits(5, 3);
        w.WriteBool(true);
        w.WriteVarUInt(300);
        w.WriteVarInt(-12345);
        w.WriteString("坦克 Tank");
        w.WriteBits(0xDEADBEEF, 32);

        var r = new BitReader(w.Data);
        Assert.Equal(5u, r.ReadBits(3));
        Assert.True(r.ReadBool());
        Assert.Equal(300u, r.ReadVarUInt());
        Assert.Equal(-12345, r.ReadVarInt());
        Assert.Equal("坦克 Tank", r.ReadString());
        Assert.Equal(0xDEADBEEF, r.ReadBits(32));
    }

    [Fact]
    public void BitReader_ThrowsOnTruncatedData()
    {
        var w = new BitWriter();
        w.WriteVarUInt(1_000_000);
        var bytes = w.Data[..1].ToArray();
        Assert.Throws<ProtocolException>(() =>
        {
            var r = new BitReader(bytes);
            r.ReadVarUInt();
        });
    }

    [Fact]
    public void WriteString_TruncatesOnCharacterBoundary()
    {
        var w = new BitWriter();
        w.WriteString("坦克坦克坦克坦克坦克", 25); // 30 bytes of UTF-8; a 25-byte cut lands inside the 9th character

        var r = new BitReader(w.Data);
        Assert.Equal("坦克坦克坦克坦克", r.ReadString(25));
    }

    [Fact]
    public void BitReader_RejectsOversizedStrings()
    {
        var w = new BitWriter();
        w.WriteString(new string('x', 60), 64);
        Assert.Throws<ProtocolException>(() =>
        {
            var r = new BitReader(w.Data.ToArray());
            r.ReadString(10);
        });
    }

    [Fact]
    public void InputPacket_RoundTripsNewestFirst()
    {
        var inputs = new[]
        {
            new InputCmd(100, 2, true, 3),
            new InputCmd(99, 1, false, 3),
            new InputCmd(98, 0, false, 2),
        };
        var w = new BitWriter();
        InputPacket.Write(w, 95, inputs);

        var r = new BitReader(w.Data.ToArray());
        Assert.Equal(MessageType.Input, (MessageType)r.ReadByte());
        var output = new InputCmd[4];
        int n = InputPacket.Read(ref r, out int ack, output);

        Assert.Equal(3, n);
        Assert.Equal(95, ack);
        Assert.Equal(inputs, output[..3]);
    }

    [Fact]
    public void LobbyAndMatchMessages_RoundTrip()
    {
        var lobby = new LobbyStateMessage(GameMode.BaseBattle, "versus-01", 1, false,
            new[] { new LobbyPlayer(0, "Ann", 0, true, true), new LobbyPlayer(1, "Bob", 1, false, false) });
        var start = new MatchStartMessage(GameMode.Coop, "coop-01", "P.\n..", 42, 3, 20, 77, new byte[] { 1, 2, 3 });
        var room = new RoomInfoMessage(1, "Room", GameMode.Deathmatch, "arena-01", 3, 8, true, 27015);

        var w = new BitWriter();
        Messages.Write(w, lobby);
        var r = new BitReader(w.Data.ToArray());
        r.ReadByte();
        var lobby2 = Messages.ReadLobbyState(ref r);
        Assert.Equal(lobby.Players, lobby2.Players);
        Assert.Equal(lobby with { Players = lobby2.Players }, lobby2);

        w.Reset();
        Messages.Write(w, start);
        r = new BitReader(w.Data.ToArray());
        r.ReadByte();
        var start2 = Messages.ReadMatchStart(ref r);
        Assert.Equal(start.Cells, start2.Cells);
        Assert.Equal(start with { Cells = start2.Cells }, start2);

        w.Reset();
        Messages.Write(w, room);
        r = new BitReader(w.Data.ToArray());
        r.ReadByte();
        Assert.Equal(room, Messages.ReadRoomInfo(ref r));
    }

    [Fact]
    public void EventBatch_RoundTrips()
    {
        var events = new[] { new GameEvent(GameEventType.TankDestroyed, 9, 100, -200, 3, 1) };
        var cells = new[] { new CellChange(12, CellType.Empty), new CellChange(400, CellType.Steel) };
        var w = new BitWriter();
        Messages.Write(w, 9, events, cells);

        var r = new BitReader(w.Data.ToArray());
        r.ReadByte();
        var batch = Messages.ReadEvents(ref r);

        Assert.Equal(9, batch.Tick);
        Assert.Equal(events, batch.Events);
        Assert.Equal(cells, batch.Cells);
    }

    [Fact]
    public void LobbyCommand_RejectsUnknownKind()
    {
        var w = new BitWriter();
        w.WriteByte((byte)MessageType.LobbyCommand);
        w.WriteByte(99);
        w.WriteString("");
        Assert.Throws<ProtocolException>(() =>
        {
            var r = new BitReader(w.Data.ToArray());
            r.ReadByte();
            Messages.ReadLobbyCommand(ref r);
        });
    }
}
