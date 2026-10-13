namespace Tank.Net.Protocol;

/// <summary>Allocation-free bit packer over a caller-owned buffer.</summary>
public sealed class BitWriter
{
    private byte[] _buffer;
    private int _bitPos;

    public BitWriter(int capacityBytes = 2048) => _buffer = new byte[capacityBytes];

    public int BitLength => _bitPos;
    public int ByteLength => (_bitPos + 7) >> 3;
    public ReadOnlySpan<byte> Data => _buffer.AsSpan(0, ByteLength);

    public void Reset()
    {
        Array.Clear(_buffer, 0, ByteLength);
        _bitPos = 0;
    }

    public void WriteBits(uint value, int bits)
    {
        if (bits is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(bits));
        EnsureCapacity(bits);
        for (int i = 0; i < bits; i++)
        {
            if (((value >> i) & 1) != 0) _buffer[_bitPos >> 3] |= (byte)(1 << (_bitPos & 7));
            _bitPos++;
        }
    }

    public void WriteBool(bool value) => WriteBits(value ? 1u : 0u, 1);

    public void WriteByte(byte value) => WriteBits(value, 8);

    /// <summary>LEB128-style: 7 data bits + continuation bit per group.</summary>
    public void WriteVarUInt(uint value)
    {
        while (value >= 0x80)
        {
            WriteBits((value & 0x7F) | 0x80, 8);
            value >>= 7;
        }
        WriteBits(value, 8);
    }

    public void WriteVarInt(int value) => WriteVarUInt((uint)((value << 1) ^ (value >> 31)));

    public void WriteString(string value, int maxBytes = 64)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
        int len = System.Math.Min(bytes.Length, maxBytes);
        // Back off continuation bytes (10xxxxxx) so a multi-byte character (e.g. Chinese) is never cut in half.
        if (len < bytes.Length)
            while (len > 0 && (bytes[len] & 0xC0) == 0x80) len--;
        WriteVarUInt((uint)len);
        for (int i = 0; i < len; i++) WriteByte(bytes[i]);
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        WriteVarUInt((uint)bytes.Length);
        foreach (byte b in bytes) WriteByte(b);
    }

    private void EnsureCapacity(int extraBits)
    {
        int needed = (_bitPos + extraBits + 7) >> 3;
        if (needed <= _buffer.Length) return;
        Array.Resize(ref _buffer, System.Math.Max(needed, _buffer.Length * 2));
    }
}
