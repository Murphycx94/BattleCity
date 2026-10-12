namespace Tank.Net.Protocol;

public enum EntityKind : byte
{
    Tank = 0,
    Bullet = 1,
    PowerUp = 2,
    Eagle = 3,
}

[Flags]
public enum EntityFlags : byte
{
    None = 0,
    Shield = 1,       // tank
    Spawning = 2,     // tank
    Moving = 4,       // tank
    Bonus = 8,        // tank
    Frozen = 16,      // tank
    Power = 32,       // bullet
    Expiring = 64,    // power-up about to vanish
    Alive = 128,      // eagle
}

/// <summary>
/// Network view of any entity. Field meaning by kind:
/// Type = TankKind / bullet speed / PowerUpType; Aux = stars (tank) / spawn input tick (bullet, -1 for AI).
/// </summary>
public struct NetEntity : IEquatable<NetEntity>
{
    public int Id;
    public EntityKind Kind;
    public byte Type;
    public byte Team;
    public byte Slot;
    public int X;
    public int Z;
    public byte Dir;
    public byte Hp;
    public EntityFlags Flags;
    public int Aux;

    public readonly bool Has(EntityFlags flag) => (Flags & flag) != 0;

    public readonly bool Equals(NetEntity o) =>
        Id == o.Id && Kind == o.Kind && Type == o.Type && Team == o.Team && Slot == o.Slot &&
        X == o.X && Z == o.Z && Dir == o.Dir && Hp == o.Hp && Flags == o.Flags && Aux == o.Aux;

    public override readonly bool Equals(object? obj) => obj is NetEntity e && Equals(e);

    public override readonly int GetHashCode() => HashCode.Combine(Id, X, Z, Dir, Hp, Flags, Aux);
}
