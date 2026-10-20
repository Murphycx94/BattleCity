using System.Numerics;
using Tank.Sim.Combat;

namespace Tank.Net.Battle;

public enum BattlePhase
{
    Connecting,
    Room,
    InMatch,
    Disconnected,
}

/// <summary>What the player wants this frame: the sticks, the aim, the trigger and the round.</summary>
public readonly record struct LocalCommand(sbyte Throttle, sbyte Steer, float AimYaw, float AimPitch, bool Fire, AmmoType Ammo = AmmoType.AP)
{
    public static readonly LocalCommand None = new(0, 0, 0f, 0f, false);
}

/// <summary>A shell to draw: the client's own (predicted) or another tank's (flown here from the server's shot event). <c>Age</c> is seconds since it left the muzzle.</summary>
public readonly record struct ShellView(int ShellId, int OwnerTankId, Vector3 Previous, Vector3 Position, Vector3 Velocity, AmmoType Ammo, bool Own, float Age = 0f);

/// <summary>Numbers for the net panel (design §5.9).</summary>
public readonly record struct BattleStats(
    int RttMs,
    double JitterMs,
    double InterpDelayMs,
    float PacketLossPercent,
    double BytesInPerSecond,
    double BytesOutPerSecond,
    int SnapshotsPerSecond,
    int SnapshotBytes,
    int Extrapolations,
    int Corrections,
    float LastCorrection,
    double MeanCorrection,
    float MaxCorrection,
    double InputSlackTicks,
    double ClockRate,
    int DroppedPackets);
