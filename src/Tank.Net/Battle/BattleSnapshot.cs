using Tank.Sim.Combat;
using Tank.Sim.Modes;

namespace Tank.Net.Battle;

[Flags]
public enum TankFlags : byte
{
    None = 0,
    Destroyed = 1,
    Protected = 2,
}

/// <summary>Another tank as a client sees it (design §5.5): whole centimetres, 12-bit headings, 10-bit gun pitch.</summary>
public struct BattleEntity : IEquatable<BattleEntity>
{
    public int Id;
    public byte Slot;      // the player's room slot
    public byte Spec;      // index into TankCatalog.All
    public byte Team;
    public int X;          // cm
    public int Z;          // cm
    public ushort Yaw;     // 12 bits
    public ushort TurretYaw;
    public ushort Pitch;   // 10 bits
    public short Speed;    // tenths of a metre per second
    public ushort Health;
    public TankFlags Flags;

    public readonly bool Has(TankFlags flag) => (Flags & flag) != 0;

    public readonly bool Equals(BattleEntity o) =>
        Id == o.Id && Slot == o.Slot && Spec == o.Spec && Team == o.Team && X == o.X && Z == o.Z && Yaw == o.Yaw &&
        TurretYaw == o.TurretYaw && Pitch == o.Pitch && Speed == o.Speed && Health == o.Health && Flags == o.Flags;

    public override readonly bool Equals(object? obj) => obj is BattleEntity e && Equals(e);

    public override readonly int GetHashCode() => HashCode.Combine(Id, X, Z, Yaw, TurretYaw, Health, Flags);
}

/// <summary>
/// The client's own tank, exactly as the server has it, for reconciliation (design §5.5); from v6 its crate layers too
/// (stage 6 design §4.4: only the owner is told how many layers a tank has), so the predictor reloads at the server's pace.
/// </summary>
public struct OwnTank : IEquatable<OwnTank>
{
    public int Id;
    public float X, Z, Yaw, TurretYaw, GunPitch, Speed, Reload, Bloom;
    public AmmoType Ammo;
    public int ApLeft, HeLeft, ShotCount, Health, ProtectedUntil, LastFired;
    public bool Destroyed;

    /// <summary>The tank's buff layers (<c>TankState.ArmorLayers</c> and the two others), 0–<see cref="CrateRules.MaxLayers"/>: 2 bits each on the wire.</summary>
    public byte ArmorLayers, ReloadLayers, OpticsLayers;

    public readonly bool Equals(OwnTank o) =>
        Id == o.Id && X == o.X && Z == o.Z && Yaw == o.Yaw && TurretYaw == o.TurretYaw && GunPitch == o.GunPitch && Speed == o.Speed &&
        Reload == o.Reload && Bloom == o.Bloom && Ammo == o.Ammo && ApLeft == o.ApLeft && HeLeft == o.HeLeft && ShotCount == o.ShotCount &&
        Health == o.Health && ProtectedUntil == o.ProtectedUntil && LastFired == o.LastFired && Destroyed == o.Destroyed &&
        ArmorLayers == o.ArmorLayers && ReloadLayers == o.ReloadLayers && OpticsLayers == o.OpticsLayers;

    public override readonly bool Equals(object? obj) => obj is OwnTank o && Equals(o);

    public override readonly int GetHashCode() => HashCode.Combine(Id, X, Z, Yaw, ShotCount, Health);
}

/// <summary>
/// A crate the viewer sees this tick (stage 6 design §4.4): its id in the match's set, its kind, whether it still stands,
/// and its ground position in whole centimetres. A row means the viewer sees the crate's <em>position</em> — the spot —
/// and <see cref="Standing"/> says whether the crate is still there: seeing an empty spot is how a client learns a crate
/// is gone. The crate table itself is never sent; a client knows only the crates it has seen.
/// </summary>
public struct CrateView : IEquatable<CrateView>
{
    public byte Id;          // 0 … CrateRules.MaxCrates − 1 (6 bits)
    public CrateKind Kind;   // 3 bits
    public bool Standing;
    public int X;            // cm
    public int Z;            // cm

    public readonly bool Equals(CrateView o) => Id == o.Id && Kind == o.Kind && Standing == o.Standing && X == o.X && Z == o.Z;

    public override readonly bool Equals(object? obj) => obj is CrateView c && Equals(c);

    public override readonly int GetHashCode() => HashCode.Combine(Id, Kind, Standing, X, Z);
}

/// <summary>The match as this client should show it (design §5.5).</summary>
public struct MatchBlock : IEquatable<MatchBlock>
{
    public MatchPhase Phase;
    public int TicksLeft;
    public bool Spotted;       // an enemy sees the client's tank
    public byte WinnerTeam;    // MatchRules.NoTeam while playing or drawn
    public byte WinnerSlot;    // MatchRules.NoSlot unless a free-for-all has ended
    public int RespawnIn;      // ticks until the client's tank comes back, 0 otherwise
    public int TeamScore0;     // the teams' kills, leavers' included (two-team modes; 0 in a free-for-all)
    public int TeamScore1;
    public byte BaseProgress0;         // how far team 1 has taken team 0's base, 0–100 (0 outside a standard match)
    public byte BaseProgress1;         // how far team 0 has taken team 1's base
    public MatchEndReason EndReason;   // the rules' EndReason: None while playing
    public byte Entrants;              // a royale: MatchRules.Entrants, the seats that joined the match ("共 N 名"), at most BattleProtocol.MaxPlayers; 0 in the other modes

    public readonly bool Equals(MatchBlock o) =>
        Phase == o.Phase && TicksLeft == o.TicksLeft && Spotted == o.Spotted && WinnerTeam == o.WinnerTeam && WinnerSlot == o.WinnerSlot
        && RespawnIn == o.RespawnIn && TeamScore0 == o.TeamScore0 && TeamScore1 == o.TeamScore1
        && BaseProgress0 == o.BaseProgress0 && BaseProgress1 == o.BaseProgress1 && EndReason == o.EndReason && Entrants == o.Entrants;

    public override readonly bool Equals(object? obj) => obj is MatchBlock m && Equals(m);

    public override readonly int GetHashCode() => HashCode.Combine(
        HashCode.Combine(Phase, TicksLeft, Spotted, WinnerTeam, WinnerSlot, RespawnIn, TeamScore0, TeamScore1), BaseProgress0, BaseProgress1, EndReason, Entrants);
}

/// <summary>One player's line on the scoreboard.</summary>
public struct PlayerStats : IEquatable<PlayerStats>
{
    public bool Active;
    public byte Team;
    public byte Spec;
    public int TankId;
    public int Kills;
    public int Deaths;
    public bool Alive;
    public byte Rank;      // 0: none yet; 1–16 once a free-for-all or a royale has ended (5 bits on the wire, 17–31 refused both ways)
    public byte Capture;   // MatchRules.CaptureBest(slot): the most points the player's tank held at once this match, 0–100
    public int Damage;     // Standing.Damage: damage dealt to other sides' tanks
    public int Lasted;     // MatchRules.LastedTicks(slot) in whole seconds

    public readonly bool Equals(PlayerStats o) =>
        Active == o.Active && Team == o.Team && Spec == o.Spec && TankId == o.TankId && Kills == o.Kills && Deaths == o.Deaths && Alive == o.Alive && Rank == o.Rank
        && Capture == o.Capture && Damage == o.Damage && Lasted == o.Lasted;

    public override readonly bool Equals(object? obj) => obj is PlayerStats p && Equals(p);

    public override readonly int GetHashCode() => HashCode.Combine(HashCode.Combine(Active, Team, TankId, Kills, Deaths, Alive, Rank, Capture), Damage, Lasted);
}

/// <summary>
/// What one client is told about one server tick: the tanks it may know of (sorted by id), its own tank in full, the
/// crates it sees (sorted by id; stage 6 design §4.4), the match, and the scoreboard. Built per client on the server;
/// kept per tick on both sides as a delta baseline.
/// </summary>
public sealed class BattleSnapshot
{
    public const int MaxEntities = Tank.Sim.Simulation.MaxTanks;

    public int Tick = -1;
    public readonly BattleEntity[] Entities = new BattleEntity[MaxEntities];
    public int EntityCount;
    public bool HasOwn;
    public OwnTank Own;
    public MatchBlock Match;
    public readonly PlayerStats[] Players = new PlayerStats[BattleProtocol.MaxPlayers];

    /// <summary>The crates the viewer sees this tick, ids strictly rising; the first <see cref="CrateCount"/> rows. 0 outside a crate mode.</summary>
    public readonly CrateView[] Crates = new CrateView[CrateRules.MaxCrates];
    public int CrateCount;

    public ReadOnlySpan<BattleEntity> EntitySpan => Entities.AsSpan(0, EntityCount);

    public ReadOnlySpan<CrateView> CrateSpan => Crates.AsSpan(0, CrateCount);

    public void CopyFrom(BattleSnapshot other)
    {
        Tick = other.Tick;
        Array.Copy(other.Entities, Entities, other.EntityCount);
        EntityCount = other.EntityCount;
        HasOwn = other.HasOwn;
        Own = other.Own;
        Match = other.Match;
        Array.Copy(other.Players, Players, Players.Length);
        Array.Copy(other.Crates, Crates, other.CrateCount);
        CrateCount = other.CrateCount;
    }

    public bool TryFind(int id, out BattleEntity entity)
    {
        int lo = 0, hi = EntityCount - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int midId = Entities[mid].Id;
            if (midId == id)
            {
                entity = Entities[mid];
                return true;
            }
            if (midId < id) lo = mid + 1;
            else hi = mid - 1;
        }
        entity = default;
        return false;
    }
}

/// <summary>Per-client feedback sent with every snapshot (prediction and clock sync).</summary>
public struct ClientFeedback
{
    public int LastProcessedInputTick;
    public int InputSlack;   // newest received input tick minus the tick it is simulated at
}
