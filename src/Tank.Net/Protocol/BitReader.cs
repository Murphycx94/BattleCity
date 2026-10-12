namespace Tank.Net.Protocol;

/// <summary>Reads what <see cref="BitWriter"/> wrote. Every read is bounds-checked.</summary>
public ref struct BitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _bitPos;

    public BitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _bitPos = 0;
    }

    public readonly int RemainingBits => _data.Length * 8 - _bitPos;

    public uint ReadBits(int bits)
    {
        if (bits is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(bits));
        if (bits > RemainingBits) throw new ProtocolException("Packet truncated.");
        uint value = 0;
        for (int i = 0; i < bits; i++)
        {
            if ((_data[_bitPos >> 3] & (1 << (_bitPos & 7))) != 0) value |= 1u << i;
            _bitPos++;
        }
        return value;
    }

    public bool ReadBool() => ReadBits(1) != 0;

    public byte ReadByte() => (byte)ReadBits(8);

    public uint ReadVarUInt()
    {
        uint result = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            uint b = ReadBits(8);
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new ProtocolException("VarUInt too long.");
    }

    public int ReadVarInt()
    {
        uint v = ReadVarUInt();
        return (int)(v >> 1) ^ -(int)(v & 1);
    }

    public string ReadString(int maxBytes = 64)
    {
        uint len = ReadVarUInt();
        if (len > maxBytes) throw new ProtocolException("String too long.");
        Span<byte> bytes = stackalloc byte[(int)len];
        for (int i = 0; i < len; i++) bytes[i] = ReadByte();
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public byte[] ReadBytes(int maxLength)
    {
        uint len = ReadVarUInt();
        if (len > maxLength) throw new ProtocolException("Byte array too long.");
        var bytes = new byte[len];
        for (int i = 0; i < len; i++) bytes[i] = ReadByte();
        return bytes;
    }
}
