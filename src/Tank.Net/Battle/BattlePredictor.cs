using System.Numerics;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Net.Battle;

/// <summary>A shot the client predicted: the shell as it flies on the client, and how the server's word on it stands.</summary>
public struct PredictedShell
{
    public Shell Shell;
    public Vector3 Previous;   // where it was a tick ago, to draw between
    public int SpawnTick;
    public bool Confirmed;     // the server reported the same shot (ShellFired on SpawnTick)
    public bool Ended;         // it struck something, here or on the server: hidden, kept until the server's events are done with it
}

/// <summary>
/// Client-side prediction of the player's own tank (design §5.7): every tick the tank is stepped with
/// <see cref="TankStep.Advance"/> (not knocking props down) on the client's battlefield, exactly as the server will
/// step it; on every snapshot the tank is rewound to the server's state and the inputs the server hasn't processed yet
/// are replayed. A small correction (under <see cref="SnapDistance"/>) is hidden by a visual offset that decays over
/// about 150 ms; a large one snaps.
/// </summary>
public sealed class BattlePredictor
{
    private const int InputHistorySize = 128;
    public const float SnapDistance = 0.5f;

    /// <summary>
    /// A reconciliation moves the tank at least this far to count as a correction, m: smaller ones are the wire's
    /// centimetre quantum and float noise from touching another tank, not a misprediction.
    /// </summary>
    public const float MinCountedCorrection = 0.02f;
    private const double OffsetTimeConstant = 0.05;   // s: 95 % gone in 150 ms
    private const float VisualOffsetEpsilonSquared = 1e-6f;   // m²: a leftover offset this small is gone

    /// <summary>
    /// An unconfirmed shot is dropped this long (1.5 s) after the render clock passed its tick — the clock the server's
    /// ShellFired events are played on; the reliable channel may hold one back a few round trips on a bad link.
    /// </summary>
    private const int OrphanShellTicks = 45;
    private const int MaxExpectedShots = 32;          // the server's shots we are waiting to predict: a few at most, never more than this

    private readonly InputCmd[] _inputs = new InputCmd[InputHistorySize];
    private readonly List<PredictedShell> _shells = new(8);
    private readonly List<(int Tick, int ShellId)> _expectedShots = new(4);   // the server's word on shots we haven't predicted yet

    private TankState _current, _previous;

    public bool HasTank { get; private set; }

    /// <summary>The own tank as predicted for the last tick.</summary>
    public TankState Current => _current;

    /// <summary>The own tank as predicted a tick before, to draw between.</summary>
    public TankState Previous => _previous;
    public Vector2 VisualOffset { get; private set; }
    public int LastPredictedTick { get; private set; } = -1;

    /// <summary>
    /// Corrections made (reconciliations of at least <see cref="MinCountedCorrection"/>, a respawn not being one), and the
    /// last, mean and largest distance corrected, m.
    /// </summary>
    public int Corrections { get; private set; }
    public float LastCorrection { get; private set; }
    public float MaxCorrection { get; private set; }
    public double MeanCorrection => Corrections == 0 ? 0 : _correctionSum / Corrections;
    private double _correctionSum;

    public IReadOnlyList<PredictedShell> Shells => _shells;

    public void Reset()
    {
        HasTank = false;
        _current = _previous = default;
        _shells.Clear();
        _expectedShots.Clear();
        VisualOffset = Vector2.Zero;
        LastPredictedTick = -1;
        Array.Clear(_inputs);
        Corrections = 0;
        LastCorrection = MaxCorrection = 0;
        _correctionSum = 0;
    }

    public InputCmd InputAt(int tick)
    {
        InputCmd cmd = _inputs[tick & (InputHistorySize - 1)];
        return cmd.Tick == tick ? cmd : BattleInputBuffer.Idle(tick, cmd);
    }

    /// <summary>Copies the newest inputs (newest first) for redundant sending.</summary>
    public int RecentInputs(Span<InputCmd> output)
    {
        int n = 0;
        for (int t = LastPredictedTick; t > LastPredictedTick - output.Length && t >= 0; t--)
        {
            InputCmd cmd = _inputs[t & (InputHistorySize - 1)];
            if (cmd.Tick != t) break;
            output[n++] = cmd;
        }
        return n;
    }

    /// <summary>Predicts one tick; <paramref name="others"/> are the other tanks' footprints as last seen.</summary>
    public void Step(in InputCmd cmd, Battlefield field, ReadOnlySpan<Obb2> others)
    {
        _inputs[cmd.Tick & (InputHistorySize - 1)] = cmd;
        LastPredictedTick = cmd.Tick;
        if (!HasTank) return;
        _previous = _current;
        if (Advance(ref _current, cmd, field, others, out TankStep.Shot shot))
        {
            var predicted = new PredictedShell { Shell = shot.Shell, Previous = shot.Shell.Position, SpawnTick = cmd.Tick };
            // Shots are matched to the server's by tick, not by a shot count: a tank fires at most once per tick, so the
            // tick names the shot (and a count would drift whenever a shot is predicted that the server never fires).
            for (int i = 0; i < _expectedShots.Count; i++)
            {
                if (_expectedShots[i].Tick != cmd.Tick) continue;
                predicted.Confirmed = true;   // the server told us first (we were behind): one shell, not two
                predicted.Shell.Id = _expectedShots[i].ShellId;
                _expectedShots.RemoveAt(i);
                break;
            }
            _shells.Add(predicted);
        }
        StepShells(field);
    }

    private static bool Advance(ref TankState tank, in InputCmd cmd, Battlefield field, ReadOnlySpan<Obb2> others, out TankStep.Shot shot) =>
        TankStep.Advance(ref tank, cmd, cmd.Tick, field, others, knockDown: false, null, canFire: cmd.Tick >= tank.ProtectedUntil, out shot);

    /// <summary>Rewinds to the server's state for the own tank and replays the inputs it has not processed yet.</summary>
    /// <param name="serverState">The own tank as the server has it, or null when the client has no tank.</param>
    public void Reconcile(TankState? serverState, int lastProcessedInputTick, bool matchEnded, Battlefield field, ReadOnlySpan<Obb2> others)
    {
        if (serverState is not { } state)
        {
            HasTank = false;
            return;
        }
        TankState replayed = state;
        int replayUntil = matchEnded ? lastProcessedInputTick : LastPredictedTick;

        // Only the last InputHistorySize inputs are kept. An ack older than that can't be replayed from (and a lying one
        // must not make the client step millions of ticks): start from the server's state and let the hard snap deal with it.
        long oldestKept = (long)replayUntil - InputHistorySize + 1;
        bool replayable = (long)lastProcessedInputTick + 1 >= oldestKept;
        if (replayable)
            for (long tick = (long)lastProcessedInputTick + 1; tick <= replayUntil; tick++) Advance(ref replayed, InputAt((int)tick), field, others, out _);

        bool respawned = _current.IsDestroyed && !replayed.IsDestroyed;   // back somewhere else: a new start, not a correction
        if (!HasTank || _current.Id != replayed.Id || respawned)
        {
            HasTank = true;
            _current = _previous = replayed;
            VisualOffset = Vector2.Zero;
            _shells.Clear();
            return;
        }
        Vector2 error = _current.Position - replayed.Position;
        float distance = error.Length();
        if (distance >= MinCountedCorrection)
        {
            Corrections++;
            LastCorrection = distance;
            MaxCorrection = MathF.Max(MaxCorrection, distance);
            _correctionSum += distance;
        }
        if (distance > 0f)
        {
            VisualOffset = distance >= SnapDistance || !replayable ? Vector2.Zero : VisualOffset + error;
            _previous.Position -= error;
        }
        _current = replayed;
    }

    public void DecayVisualOffset(double dt)
    {
        VisualOffset *= (float)System.Math.Exp(-dt / OffsetTimeConstant);
        if (VisualOffset.LengthSquared() < VisualOffsetEpsilonSquared) VisualOffset = Vector2.Zero;
    }

    /// <summary>
    /// The server reported a shot of ours on <paramref name="tick"/>: the shell predicted for that tick is confirmed and
    /// takes the server's id. A tick not predicted yet (the client is behind after a hard resync) is remembered, and
    /// the shell confirmed as it is predicted.
    /// </summary>
    /// <returns>False when the tick is past and no predicted shell matches (the server's shell is then shown like anyone else's).</returns>
    public bool Confirm(int tick, int shellId)
    {
        if (tick > LastPredictedTick)
        {
            if (_expectedShots.Count >= MaxExpectedShots) _expectedShots.RemoveAt(0);
            _expectedShots.Add((tick, shellId));
            return true;
        }
        for (int i = 0; i < _shells.Count; i++)   // by tick, as in Step: one shot per tank per tick
        {
            PredictedShell s = _shells[i];
            if (s.SpawnTick != tick || s.Confirmed) continue;
            s.Confirmed = true;
            s.Shell.Id = shellId;
            _shells[i] = s;
            return true;
        }
        return false;
    }

    /// <summary>The server says shell <paramref name="shellId"/> is done (burst, hit): hide it.</summary>
    public void End(int shellId)
    {
        for (int i = 0; i < _shells.Count; i++)
        {
            if (_shells[i].Shell.Id != shellId) continue;
            PredictedShell s = _shells[i];
            s.Ended = true;
            _shells[i] = s;
        }
    }

    /// <summary>
    /// Drops shots the server never confirmed once the render clock is well past their tick (the clock the server's
    /// events play on, so a confirmation still on its way is not mistaken for none), and ended shells once confirmed.
    /// </summary>
    public void Prune(double renderTick)
    {
        for (int i = _shells.Count - 1; i >= 0; i--)
        {
            PredictedShell s = _shells[i];
            bool orphan = !s.Confirmed && renderTick > s.SpawnTick + OrphanShellTicks;
            bool done = s.Ended && s.Confirmed;
            if (orphan || done) _shells.RemoveAt(i);
        }
        for (int i = _expectedShots.Count - 1; i >= 0; i--)
            if (_expectedShots[i].Tick <= LastPredictedTick) _expectedShots.RemoveAt(i);   // predicted since, and no shot came of it
    }

    private void StepShells(Battlefield field)
    {
        for (int i = 0; i < _shells.Count; i++)
        {
            PredictedShell s = _shells[i];
            if (s.Ended) continue;
            s.Previous = s.Shell.Position;
            ShellStep step = Ballistics.Step(ref s.Shell, field, out _);
            if (step != ShellStep.Flying) s.Ended = true;   // struck the ground, a rock or a wall here; the server's burst event follows
            _shells[i] = s;
        }
    }
}
