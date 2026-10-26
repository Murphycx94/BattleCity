using System.Globalization;
using Tank.Sim.Maps;

namespace Tank.Client.Overlay;

/// <summary>
/// The ground hint over the vehicle name (riverland design §6.7): what the player drives on and the top-speed factor
/// it gives, "公路 ×1.15", "沙地 ×0.80", or in water "水深 0.8 m ×0.73". The depth shows in tenths of a metre and the
/// factor in hundredths, so equal inputs at that precision give equal text. Engine-neutral so Tank.Art.Tests can check it.
/// </summary>
public static class GroundText
{
    /// <summary>
    /// The hint for <paramref name="kind"/> under <paramref name="waterDepth"/> m of water at top-speed factor
    /// <paramref name="factor"/>; null when the factor is 1 (within 0.005) or not a finite number. Numbers are
    /// invariant-culture and rounded half away from zero (0.25 m shows as 0.3); a depth under 0.1 m shows as 0.1, and a
    /// depth that is negative or not finite counts as dry ground. The factor is the simulation's
    /// (<see cref="Battlefield.SpeedFactorAt"/>), not a client table.
    /// </summary>
    public static string? Of(SurfaceKind kind, float waterDepth, float factor)
    {
        if (!float.IsFinite(factor)) return null;
        int hundredths = (int)MathF.Round(factor * 100f, MidpointRounding.AwayFromZero);
        if (hundredths == 100) return null;
        if (float.IsFinite(waterDepth) && waterDepth > 0f)
        {
            int tenths = Math.Max(1, (int)MathF.Round(waterDepth * 10f, MidpointRounding.AwayFromZero));
            return string.Create(CultureInfo.InvariantCulture, $"水深 {tenths / 10}.{tenths % 10} m ×{hundredths / 100}.{hundredths % 100:D2}");
        }
        return string.Create(CultureInfo.InvariantCulture, $"{Name(kind)} ×{hundredths / 100}.{hundredths % 100:D2}");
    }

    private static string Name(SurfaceKind kind) => kind switch
    {
        SurfaceKind.Road => "公路",
        SurfaceKind.Sand => "沙地",
        SurfaceKind.Dirt => "土地",
        _ => "草地",
    };
}
