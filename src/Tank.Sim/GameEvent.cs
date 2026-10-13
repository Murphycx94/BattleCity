using System.Numerics;

namespace Tank.Sim;

public enum GameEventType : byte
{
    ShellFired,
    ShellExploded,
}

/// <summary>Something presentation should react to. <see cref="Velocity"/> is zero when not meaningful.</summary>
public readonly record struct GameEvent(GameEventType Type, int Tick, int TankId, int ShellId, Vector3 Position, Vector3 Velocity);
