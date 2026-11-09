using System.Numerics;

namespace Tank.Sim.Modes;

/// <summary>One team's base (stage 5 design §3.2): a circle on the ground the other team captures. X is world X, Y world Z.</summary>
public readonly record struct TeamBase(byte Team, Vector2 Centre, float Radius)
{
    /// <summary>Every base on the maps in the catalog is this big.</summary>
    public const float StandardRadius = 25f;

    /// <summary>The largest radius <see cref="Check"/> allows.</summary>
    public const float MaxRadius = 100f;

    /// <summary>
    /// Null or empty: no bases. Otherwise exactly two, one for team 0 and one for team 1 (any order), each with a finite
    /// centre and a radius in (0, 100]; ArgumentException otherwise. Returns the list (empty for null).
    /// </summary>
    public static IReadOnlyList<TeamBase> Check(IReadOnlyList<TeamBase>? bases, string paramName)
    {
        if (bases is null || bases.Count == 0) return Array.Empty<TeamBase>();
        if (bases.Count != 2) throw new ArgumentException($"Expected no bases or two, got {bases.Count}.", paramName);
        bool team0 = false, team1 = false;
        foreach (TeamBase b in bases)
        {
            if (b.Team > 1) throw new ArgumentException($"A base for team {b.Team}: only teams 0 and 1 have one.", paramName);
            ref bool seen = ref b.Team == 0 ? ref team0 : ref team1;
            if (seen) throw new ArgumentException($"Two bases for team {b.Team}.", paramName);
            seen = true;
            if (!float.IsFinite(b.Centre.X) || !float.IsFinite(b.Centre.Y))
                throw new ArgumentException($"Team {b.Team}'s base centre {b.Centre} is not finite.", paramName);
            if (!(b.Radius > 0 && b.Radius <= MaxRadius))
                throw new ArgumentException($"Team {b.Team}'s base radius {b.Radius} is not in (0, {MaxRadius}].", paramName);
        }
        return bases;
    }
}
