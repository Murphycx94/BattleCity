using Tank.Net.Protocol;
using Tank.Sim;
using Tank.Sim.Combat;

namespace Tank.Net.Battle;

/// <summary>
/// A client's input (design §5.3): the newest few commands, newest first (redundancy against loss), and the newest
/// snapshot tick the client has, which the server takes as the delta baseline. Throttle and steer take 2 bits each,
/// the aim 16 bits for yaw and 16 for pitch, fire and ammunition a bit each: 5 bytes a command. A client must predict
/// with the quantised aim (<see cref="BattleProtocol.QuantizedAim"/>) the server will read here.
/// </summary>
public static class BattleInputPacket
{
    public static void Write(BitWriter w, int ackSnapshotTick, ReadOnlySpan<InputCmd> inputs)
    {
        int count = System.Math.Min(inputs.Length, BattleProtocol.MaxRedundantInputs);
        if (count == 0) throw new ArgumentException("At least one input is required.", nameof(inputs));
        w.WriteByte((byte)BattleMessage.Input);
        w.WriteVarUInt((uint)inputs[0].Tick);
        w.WriteVarUInt((uint)System.Math.Max(0, ackSnapshotTick));
        w.WriteBits((uint)count, 3);
        for (int i = 0; i < count; i++)
        {
            InputCmd c = inputs[i];
            w.WriteBits((uint)(System.Math.Sign(c.Throttle) + 1), 2);
            w.WriteBits((uint)(System.Math.Sign(c.Steer) + 1), 2);
            w.WriteBits(BattleProtocol.QuantizeAngle(c.AimYaw, BattleProtocol.AimBits), BattleProtocol.AimBits);
            w.WriteBits(BattleProtocol.QuantizePitch(c.AimPitch, BattleProtocol.AimBits), BattleProtocol.AimBits);
            w.WriteBool(c.Fire);
            w.WriteBits(c.Ammo == AmmoType.HE ? 1u : 0u, 1);
        }
    }

    /// <summary>Decodes into <paramref name="output"/> (newest first; ticks count down from the newest) and returns the count.</summary>
    public static int Read(ref BitReader r, out int ackSnapshotTick, Span<InputCmd> output)
    {
        int newest = BattleMessages.ReadNonNegative(ref r);
        ackSnapshotTick = BattleMessages.ReadNonNegative(ref r);
        int count = (int)r.ReadBits(3);
        if (count == 0 || count > BattleProtocol.MaxRedundantInputs || count > output.Length) throw new ProtocolException("Bad input count.");
        for (int i = 0; i < count; i++)
        {
            uint throttle = r.ReadBits(2), steer = r.ReadBits(2);
            if (throttle > 2 || steer > 2) throw new ProtocolException("Bad axis value.");
            float yaw = BattleProtocol.DequantizeAngle(r.ReadBits(BattleProtocol.AimBits), BattleProtocol.AimBits);
            float pitch = BattleProtocol.DequantizePitch(r.ReadBits(BattleProtocol.AimBits), BattleProtocol.AimBits);
            bool fire = r.ReadBool();
            AmmoType ammo = r.ReadBits(1) == 1 ? AmmoType.HE : AmmoType.AP;
            output[i] = new InputCmd(newest - i, (sbyte)((int)throttle - 1), (sbyte)((int)steer - 1), yaw, pitch, fire, ammo);
        }
        return count;
    }
}
