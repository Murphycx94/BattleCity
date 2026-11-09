using Tank.Net.Protocol;

namespace Tank.Net.Tests.Protocol;

public sealed class BitStreamTests
{
    [Fact]
    public void Reserve_grows_the_buffer_once_and_writes_nothing()
    {
        var w = new BitWriter(16);
        w.WriteByte(7);
        w.Reserve(1000);
        Assert.Equal(1001, w.Capacity);    // what is needed, not the doubling's 32
        Assert.Equal(8, w.BitLength);
        w.Reserve(500);                    // already room: nothing changes
        Assert.Equal(1001, w.Capacity);
        w.WriteAligned(new byte[1000]);
        Assert.Equal(1001, w.Capacity);
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Reserve(-1));
    }

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
}
