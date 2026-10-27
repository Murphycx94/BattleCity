using System.Numerics;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Navigation;

/// <summary>
/// Something in the way that the <see cref="NavGrid"/> can't see, for one search (5C batch B review follow-up): a wreck or
/// a tank that has stood still, as a disc round its <paramref name="Centre"/> of the body's own <paramref name="Radius"/>
/// (its hull's half-diagonal). The search closes the cells whose centres lie within Radius + <see cref="NavGrid.Clearance"/>
/// of the centre and keeps its smoothed segments Radius + <see cref="PathFinder.HullClearance"/> off it, as it does round a
/// rock (<see cref="PathFinder.Begin(Vector2, Vector2, ReadOnlySpan{NavDisc})"/>).
/// </summary>
public readonly record struct NavDisc(Vector2 Centre, float Radius)
{
    /// <summary>A tank's own radius: half its hull's diagonal, m.</summary>
    public static float RadiusOf(TankSpec spec) => 0.5f * MathF.Sqrt(spec.HullLength * spec.HullLength + spec.HullWidth * spec.HullWidth);

    /// <summary>A finite centre and a finite radius of 0 or more.</summary>
    internal bool IsValid => float.IsFinite(Centre.X) && float.IsFinite(Centre.Y) && float.IsFinite(Radius) && Radius >= 0f;
}
