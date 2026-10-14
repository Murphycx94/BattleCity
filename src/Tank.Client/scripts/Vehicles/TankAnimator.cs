namespace Tank.Client.Vehicles;

/// <summary>
/// Visual-only motion state for one tank: how far each track has rolled, the hull's pitch nod under acceleration,
/// and barrel recoil. Engine-neutral so it can be unit tested; <see cref="TankModel"/> applies it to nodes.
/// </summary>
public sealed class TankAnimator
{
    public const float RecoilDistance = 0.4f;
    public const float RecoilReturnTime = 0.35f;
    public static readonly float MaxNod = 2f * MathF.PI / 180f;

    private const float NodPerAcceleration = 0.0045f; // rad per m/s² (4 m/s² → ~1°)
    private const float NodStiffness = 70f;
    private const float NodDamping = 11f;            // a little under critical: one soft overshoot
    private const float FireNodImpulse = 0.25f;     // rad/s kick from a forward shot; peaks near the 2° limit
    private const float MaxSubstep = 1f / 30f;
    private const float AccelerationSmoothing = 8f;  // 1/s

    private readonly float _trackHalfSpacing;
    private float _lastSpeed;
    private bool _hasSpeed;
    private float _acceleration;
    private float _nodVelocity;
    private float _sinceFire = float.PositiveInfinity;

    /// <param name="trackX">Distance from the hull centre line to each track's centre.</param>
    public TankAnimator(float trackX) => _trackHalfSpacing = trackX;

    /// <summary>Distance the left track has rolled forward (m); the wheels on that side turn by this over their radius.</summary>
    public float LeftDistance { get; private set; }

    public float RightDistance { get; private set; }

    /// <summary>Hull pitch in radians, positive = nose up. Never beyond ±<see cref="MaxNod"/>.</summary>
    public float Nod { get; private set; }

    /// <summary>Hull pitch velocity, rad/s.</summary>
    public float NodVelocity => _nodVelocity;

    /// <summary>How far the barrel is pushed back right now (0 … <see cref="RecoilDistance"/>).</summary>
    public float Recoil =>
        _sinceFire >= RecoilReturnTime ? 0f : RecoilDistance * Square(1f - _sinceFire / RecoilReturnTime);

    /// <summary>Restarts barrel recoil and gives the hull a small rock; <paramref name="turretYaw"/> (hull-relative) scales it, so a broadside shot barely pitches the hull.</summary>
    public void Fire(float turretYaw = 0f)
    {
        _sinceFire = 0f;
        _nodVelocity += FireNodImpulse * MathF.Cos(turretYaw);
    }

    /// <param name="speed">Forward speed, m/s (negative when reversing).</param>
    /// <param name="yawRate">Turn rate, rad/s, positive = turning left.</param>
    public void Step(float speed, float yawRate, float dt)
    {
        if (dt <= 0) return;
        // A left turn speeds up the right track: v = speed + ω·x for a track at x (right is +x).
        LeftDistance += (speed - yawRate * _trackHalfSpacing) * dt;
        RightDistance += (speed + yawRate * _trackHalfSpacing) * dt;
        _sinceFire += dt;

        float raw = _hasSpeed ? (speed - _lastSpeed) / dt : 0f;
        _lastSpeed = speed;
        _hasSpeed = true;

        // The spring is integrated in substeps: one long frame would otherwise overshoot the explicit-Euler stability limit.
        for (float remaining = dt; remaining > 0f;)
        {
            float h = MathF.Min(remaining, MaxSubstep);
            remaining -= h;
            _acceleration += (raw - _acceleration) * MathF.Min(1f, AccelerationSmoothing * h);
            // Speeding up lifts the nose (the hull squats on its rear springs); braking dips it.
            float target = Math.Clamp(_acceleration * NodPerAcceleration, -MaxNod, MaxNod);
            _nodVelocity += ((target - Nod) * NodStiffness - _nodVelocity * NodDamping) * h;
            float next = Nod + _nodVelocity * h;
            Nod = Math.Clamp(next, -MaxNod, MaxNod);
            if (Nod != next) _nodVelocity = 0f; // hit the stop
        }
    }

    private static float Square(float x) => x * x;
}
