using Tank.Core.World;

namespace Tank.Net.Protocol;

/// <summary>
/// Client input: the newest few commands (redundancy against packet loss) plus the newest snapshot tick
/// the client has received, which the server uses as the delta baseline.
/// </summary>
public static class InputPacket
{
    private const int MaxViewDelta = 15;

    /// <param name="inputs">Newest first. At most <see cref="ProtocolInfo.MaxRedundantInputs"/> are sent.</param>
    public static void Write(BitWriter w, int ackSnapshotTick, ReadOnlySpan<InputCmd> inputs)
    {
        int count = System.Math.Min(inputs.Length, ProtocolInfo.MaxRedundantInputs);
        if (count == 0) throw new ArgumentException("At least one input is required.", nameof(inputs));
        w.WriteByte((byte)MessageType.Input);
        w.WriteVarUInt((uint)inputs[0].Tick);
        w.WriteVarUInt((uint)System.Math.Max(0, ackSnapshotTick));
        w.WriteBits((uint)count, 3);
        for (int i = 0; i < count; i++)
        {
            w.WriteBits(inputs[i].Move, 3);
            w.WriteBool(inputs[i].Fire);
            w.WriteBits((uint)System.Math.Min((int)inputs[i].ViewDelta, MaxViewDelta), 4);
        }
    }

    /// <summary>Decodes into <paramref name="output"/> (newest first) and returns the count.</summary>
    public static int Read(ref BitReader r, out int ackSnapshotTick, Span<InputCmd> output)
    {
        int newest = (int)r.ReadVarUInt();
        ackSnapshotTick = (int)r.ReadVarUInt();
        int count = (int)r.ReadBits(3);
        if (count == 0 || count > ProtocolInfo.MaxRedundantInputs || count > output.Length)
            throw new ProtocolException("Bad input count.");
        for (int i = 0; i < count; i++)
        {
            byte move = (byte)r.ReadBits(3);
            if (move > 4) throw new ProtocolException("Bad move value.");
            bool fire = r.ReadBool();
            byte view = (byte)r.ReadBits(4);
            output[i] = new InputCmd(newest - i, move, fire, view);
        }
        return count;
    }
}
