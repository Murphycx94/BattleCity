using Tank.Core.World;
using Tank.Net.Protocol;

namespace Tank.Net.Tests.Protocol;

/// <summary>The old game's room-info reply is read off the LAN from anyone: its port must fit an endpoint.</summary>
public sealed class LegacyRoomInfoTests
{
    private static RoomInfoMessage ReadBack(int port)
    {
        var w = new BitWriter();
        Messages.Write(w, new RoomInfoMessage(1, "Room", GameMode.Deathmatch, "arena-01", 3, 8, true, port));
        var r = new BitReader(w.Data.ToArray());
        r.ReadByte();
        return Messages.ReadRoomInfo(ref r);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(27015)]
    [InlineData(65535)]
    public void A_port_that_fits_an_endpoint_is_read(int port) => Assert.Equal(port, ReadBack(port).Port);

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]   // a varint of 2^32 - 1, which the int cast turns negative
    public void A_port_outside_1_to_65535_is_a_protocol_error_not_an_argument_error_later(int port) =>
        Assert.Throws<ProtocolException>(() => ReadBack(port));
}
