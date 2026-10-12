using Tank.Core;
using Tank.Core.Math;
using Tank.Core.Rules;
using Tank.Core.Systems;
using Tank.Core.World;
using Tank.Net.Protocol;

namespace Tank.Net.Client;

/// <summary>
/// Client-side prediction for the local tank and its bullets (docs/03-netcode.md §4, §6.1).
/// Runs the exact same <see cref="TankMovement"/> code as the server; on every snapshot it rewinds to the
/// authoritative state and replays unacknowledged inputs. Any correction is hidden by a decaying visual offset.
/// </summary>
public sealed class ClientPredictor
{
    private const int InputHistorySize = 128;
    private const double VisualOffsetTimeConstant = 0.06;   // seconds
    private const int OrphanBulletTimeoutTicks = 60;

    private readonly InputCmd[] _inputs = new InputCmd[InputHistorySize];
    private readonly List<PredictedBullet> _bullets = new(8);
    private int _ownTankId;

    public bool HasTank { get; private set; }
    public TankState Current;
    public TankState Previous;
    public double VisualOffsetX { get; private set; }
    public double VisualOffsetZ { get; private set; }
    public int LastPredictedTick { get; private set; } = -1;
    public int Mispredictions { get; private set; }
    public IReadOnlyList<PredictedBullet> Bullets => _bullets;

    /// <summary>Raised on every non-zero reconciliation: (snapshot tick, dx, dz). For diagnostics and net_graph logs.</summary>
    public event Action<int, int, int>? Corrected;

    public void Reset()
    {
        HasTank = false;
        _ownTankId = 0;
        _bullets.Clear();
        VisualOffsetX = VisualOffsetZ = 0;
        LastPredictedTick = -1;
        Array.Clear(_inputs);
    }

    public InputCmd InputAt(int tick)
    {
        var cmd = _inputs[tick & (InputHistorySize - 1)];
        return cmd.Tick == tick ? cmd : InputCmd.Idle(tick);
    }

    /// <summary>Copies the newest inputs (newest first) for redundant sending.</summary>
    public int RecentInputs(Span<InputCmd> output)
    {
        int n = 0;
        for (int t = LastPredictedTick; t > LastPredictedTick - output.Length && t >= 0; t--)
        {
            var cmd = _inputs[t & (InputHistorySize - 1)];
            if (cmd.Tick != t) break;
            output[n++] = cmd;
        }
        return n;
    }

    /// <summary>Predicts one tick. <paramref name="hitTargets"/> are hostile tank boxes as currently rendered.</summary>
    public void Step(in InputCmd cmd, GameMap map, ReadOnlySpan<Box> obstacles, ReadOnlySpan<Box> hitTargets,
        bool frozen, List<GameEvent> localEvents)
    {
        _inputs[cmd.Tick & (InputHistorySize - 1)] = cmd;
        LastPredictedTick = cmd.Tick;

        if (HasTank)
        {
            Previous = Current;
            StepTank(ref Current, cmd, map, obstacles, frozen);
            if (cmd.Fire && !frozen && CanFire()) Fire(cmd, localEvents);
        }
        StepBullets(cmd.Tick, map, hitTargets, localEvents);
    }

    /// <summary>Rewinds to the server's state for our tank and replays inputs it has not processed yet.</summary>
    public void Reconcile(Snapshot snap, in ClientHeader header, int mySlot, GameMap map, ReadOnlySpan<Box> obstacles,
        List<GameEvent> localEvents)
    {
        PairBullets(snap, mySlot, localEvents);

        int tankId = (uint)mySlot < (uint)snap.Players.Length ? snap.Players[mySlot].TankId : 0;
        if (tankId == 0 || !snap.TryFind(tankId, out var e))
        {
            HasTank = false;
            _ownTankId = 0;
            return;
        }

        var replayed = FromNet(e, header);
        bool frozen = e.Has(EntityFlags.Frozen) || snap.Phase == MatchPhase.Ended;
        // A finished match no longer simulates, so pending inputs will never be applied: do not replay them.
        int replayUntil = snap.Phase == MatchPhase.Ended ? header.LastProcessedInputTick : LastPredictedTick;
        for (int tick = header.LastProcessedInputTick + 1; tick <= replayUntil; tick++)
        {
            var cmd = InputAt(tick);
            StepTank(ref replayed, cmd, map, obstacles, frozen);
            if (cmd.Fire && replayed.FireCooldown == 0 && HasBulletFrom(tick)) replayed.FireCooldown = TankStats.FireCooldownTicks;
        }

        if (!HasTank || _ownTankId != tankId)
        {
            // Fresh spawn: no smoothing, just appear.
            HasTank = true;
            _ownTankId = tankId;
            Current = Previous = replayed;
            VisualOffsetX = VisualOffsetZ = 0;
            return;
        }

        int dx = Current.X - replayed.X, dz = Current.Z - replayed.Z;
        if (dx != 0 || dz != 0)
        {
            Mispredictions++;
            Corrected?.Invoke(snap.Tick, dx, dz);
            VisualOffsetX += dx;
            VisualOffsetZ += dz;
            Previous.X -= dx;
            Previous.Z -= dz;
            if (System.Math.Abs(VisualOffsetX) > Units.Tile || System.Math.Abs(VisualOffsetZ) > Units.Tile)
                VisualOffsetX = VisualOffsetZ = 0; // too far to hide: snap
        }
        Current = replayed;
    }

    public void DecayVisualOffset(double dt)
    {
        double k = System.Math.Exp(-dt / VisualOffsetTimeConstant);
        VisualOffsetX *= k;
        VisualOffsetZ *= k;
        if (System.Math.Abs(VisualOffsetX) < 0.5) VisualOffsetX = 0;
        if (System.Math.Abs(VisualOffsetZ) < 0.5) VisualOffsetZ = 0;
    }

    private static void StepTank(ref TankState t, in InputCmd cmd, GameMap map, ReadOnlySpan<Box> obstacles, bool frozen)
    {
        // Same order as the server: timers first, then movement.
        if (t.ShieldTicks > 0) t.ShieldTicks--;
        if (t.SpawnTicks > 0) t.SpawnTicks--;
        if (t.FireCooldown > 0) t.FireCooldown--;
        TankMovement.Step(ref t, cmd, frozen, map, obstacles);
    }

    private bool CanFire()
    {
        if (Current.SpawnTicks > 0 || Current.FireCooldown > 0) return false;
        int alive = 0;
        foreach (var b in _bullets) if (!b.LocallyEnded) alive++;
        return alive < TankStats.MaxBullets(Current);
    }

    private void Fire(in InputCmd cmd, List<GameEvent> localEvents)
    {
        var (x, z) = BulletPhysics.MuzzlePosition(Current);
        _bullets.Add(new PredictedBullet
        {
            SpawnTick = cmd.Tick,
            State = new BulletState
            {
                OwnerTankId = Current.Id, OwnerSlot = Current.Slot, Team = Current.Team, X = x, Z = z, Dir = Current.Dir,
                Speed = (byte)TankStats.BulletSpeed(Current), Power = TankStats.PowerBullet(Current), SpawnInputTick = cmd.Tick,
            },
        });
        Current.FireCooldown = TankStats.FireCooldownTicks;
        localEvents.Add(new GameEvent(GameEventType.Fired, cmd.Tick, x, z, Current.Slot));
    }

    private void StepBullets(int tick, GameMap map, ReadOnlySpan<Box> hitTargets, List<GameEvent> localEvents)
    {
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_bullets);
        for (int i = 0; i < span.Length; i++)
        {
            ref var pb = ref span[i];
            if (pb.LocallyEnded) continue;
            int remaining = pb.State.Speed;
            while (remaining > 0 && !pb.LocallyEnded)
            {
                int step = System.Math.Min(remaining, Units.BulletSubstep);
                remaining -= step;
                pb.State.X += pb.State.Dir.Dx() * step;
                pb.State.Z += pb.State.Dir.Dz() * step;
                Box box = pb.State.Box;
                if (BulletPhysics.OutOfBounds(map, box) || BulletPhysics.HitsTerrain(map, box) || HitsAny(box, hitTargets))
                {
                    pb.LocallyEnded = true;
                    pb.EndedTick = tick;
                    localEvents.Add(new GameEvent(GameEventType.BulletSpark, tick, pb.State.X, pb.State.Z, (int)pb.State.Dir, pb.State.OwnerSlot));
                }
            }
        }
    }

    private void PairBullets(Snapshot snap, int mySlot, List<GameEvent> localEvents)
    {
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            var pb = _bullets[i];
            if (pb.SpawnTick > snap.Tick)
            {
                continue; // server has not simulated that tick yet
            }
            if (FindServerBullet(snap, mySlot, pb.SpawnTick))
            {
                if (!pb.Confirmed)
                {
                    pb.Confirmed = true;
                    _bullets[i] = pb;
                }
                if (pb.LocallyEnded && snap.Tick - pb.EndedTick > OrphanBulletTimeoutTicks) _bullets.RemoveAt(i);
                continue;
            }
            // Server says this bullet no longer exists (or never did): end it where we show it.
            if (!pb.LocallyEnded)
                localEvents.Add(new GameEvent(GameEventType.BulletSpark, snap.Tick, pb.State.X, pb.State.Z, (int)pb.State.Dir, pb.State.OwnerSlot));
            _bullets.RemoveAt(i);
        }
    }

    private bool HasBulletFrom(int tick)
    {
        foreach (var b in _bullets) if (b.SpawnTick == tick) return true;
        return false;
    }

    private static bool FindServerBullet(Snapshot snap, int mySlot, int spawnTick)
    {
        foreach (ref readonly var e in snap.EntitySpan)
            if (e.Kind == EntityKind.Bullet && e.Slot == mySlot && e.Aux == spawnTick) return true;
        return false;
    }

    private static bool HitsAny(in Box box, ReadOnlySpan<Box> targets)
    {
        foreach (ref readonly var t in targets) if (t.Overlaps(box)) return true;
        return false;
    }

    private static TankState FromNet(in NetEntity e, in ClientHeader h) => new()
    {
        Id = e.Id,
        Kind = (TankKind)e.Type,
        Team = e.Team,
        Slot = e.Slot,
        X = e.X,
        Z = e.Z,
        Dir = (Dir)e.Dir,
        Hp = e.Hp,
        Stars = (byte)e.Aux,
        ShieldTicks = e.Has(EntityFlags.Shield) ? (ushort)1 : (ushort)0,
        SpawnTicks = h.OwnSpawnTicks,
        SlideTicks = h.OwnSlideTicks,
        FireCooldown = h.OwnFireCooldown,
        Moving = e.Has(EntityFlags.Moving),
    };
}
