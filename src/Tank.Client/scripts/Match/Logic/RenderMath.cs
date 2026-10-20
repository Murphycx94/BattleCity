using System.Numerics;
using Tank.Client.Sandbox;
using Tank.Net.Battle;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Client.Match;

/// <summary>
/// The sums between what <c>BattleClient</c> knows and what the match scene draws (blueprint §3.2): the own tank
/// between its last two predicted ticks at the position the client wants it drawn (prediction plus the correction being
/// smoothed away), a track-animation yaw rate from two headings, and another tank's wire data as a <see cref="TankState"/>.
/// </summary>
public static class RenderMath
{
    /// <summary>The yaw rate the tracks animate at is clamped to this, rad/s: a snap or respawn must not spin them.</summary>
    public const float MaxYawRate = 3f;

    /// <summary>The own tank <paramref name="alpha"/> of the way from <paramref name="prev"/> to <paramref name="cur"/>, drawn at <paramref name="renderPos"/>.</summary>
    public static TankState OwnDisplay(in TankState prev, in TankState cur, float alpha, Vector2 renderPos) =>
        TankInterpolation.Between(prev, cur, alpha) with { Position = renderPos };

    /// <summary>The turn rate from <paramref name="prevYaw"/> to <paramref name="yaw"/> over <paramref name="dt"/> s, along the shorter arc, clamped to ±<see cref="MaxYawRate"/>; 0 when no time passed.</summary>
    public static float YawRate(float prevYaw, float yaw, float dt)
    {
        if (dt <= 0f) return 0f;
        return System.Math.Clamp(Angles.Wrap(yaw - prevYaw) / dt, -MaxYawRate, MaxYawRate);
    }

    /// <summary>Another tank as a <see cref="TankState"/>: the pose from the interpolator, spec, team, health and flags from its entity. What the wire doesn't carry (reload, ammunition) stays zero.</summary>
    public static TankState FromRemote(in RemoteTank r)
    {
        BattleEntity e = r.Entity;
        return new TankState
        {
            Id = e.Id,
            Spec = TankCatalog.All[e.Spec],
            Team = e.Team,
            Position = r.Position,
            Yaw = r.Yaw,
            TurretYaw = r.TurretYaw,
            GunPitch = r.GunPitch,
            Speed = r.Speed,
            Health = e.Health,
            IsDestroyed = e.Has(TankFlags.Destroyed),
            ProtectedUntil = e.Has(TankFlags.Protected) ? int.MaxValue : 0,
            LastFired = TankState.NeverFired,
        };
    }
}

/// <summary>
/// Notices the own tank firing from its predicted <c>ShotCount</c> (blueprint §3.3): a predicted shot never reaches the
/// event queue, so the muzzle flash, recoil and camera shake key off the count going up. Fires once per increase, never
/// on a decrease (a respawn resets the count), and starts afresh on another tank id.
/// </summary>
public sealed class ShotDetector
{
    private int _tankId;
    private int _count;
    private bool _primed;

    /// <returns>True once when <paramref name="shotCount"/> rose since the last call for the same <paramref name="tankId"/>.</returns>
    public bool Update(int shotCount, int tankId)
    {
        if (!_primed || tankId != _tankId)
        {
            (_tankId, _count, _primed) = (tankId, shotCount, true);
            return false;
        }
        bool fired = shotCount > _count;
        _count = shotCount;
        return fired;
    }
}
