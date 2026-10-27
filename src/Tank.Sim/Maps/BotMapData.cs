using System.Collections.ObjectModel;
using System.Numerics;
using Tank.Sim.Modes;

namespace Tank.Sim.Maps;

/// <summary>An attack lane (stage 5 design §4.2): via points from team 0's side to team 1's; team 1 drives it backwards.</summary>
public sealed record BotLane(string Name, IReadOnlyList<Vector2> Via);

/// <summary>
/// What bots need to know about a map besides its waypoints and bases (stage 5 design §4.2): attack lanes and each team's
/// cover points. Every list is copied in and handed out read-only (wrappers made once here, so no cast can change the
/// data and no call allocates); a lane is stored once, from team 0's side, and its reversal for team 1 is built here.
/// </summary>
public sealed class BotMapData
{
    public const int MinLanes = 2, MaxLanes = 3;
    public const int MinVia = 1, MaxVia = 8;
    public const int MinCover = 3, MaxCover = 5;

    /// <summary>No lanes and no cover points: a map the bots know nothing extra about.</summary>
    public static readonly BotMapData None = new(Array.Empty<BotLane>(), Array.Empty<Vector2>(), Array.Empty<Vector2>());

    private readonly ReadOnlyCollection<BotLane> _lanes;
    private readonly ReadOnlyCollection<Vector2>[] _via0, _via1;   // per lane: team 0's order, and reversed for team 1
    private readonly ReadOnlyCollection<Vector2> _cover0, _cover1;

    /// <param name="lanes">None, or 2–3 lanes, each named, with 1–8 finite via points.</param>
    /// <param name="cover0">Team 0's cover points: none (and then cover1 none too), or 3–5 finite points.</param>
    /// <param name="cover1">Team 1's cover points, on the same terms.</param>
    /// <exception cref="ArgumentException">Anything else.</exception>
    public BotMapData(IReadOnlyList<BotLane> lanes, IReadOnlyList<Vector2> cover0, IReadOnlyList<Vector2> cover1)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(cover0);
        ArgumentNullException.ThrowIfNull(cover1);
        if (lanes.Count != 0 && (lanes.Count < MinLanes || lanes.Count > MaxLanes))
            throw new ArgumentException($"Expected no lanes or {MinLanes}–{MaxLanes}, got {lanes.Count}.", nameof(lanes));
        if ((cover0.Count == 0) != (cover1.Count == 0))
            throw new ArgumentException($"Either both teams have cover points or neither: {cover0.Count} and {cover1.Count}.", nameof(cover1));

        var copies = new BotLane[lanes.Count];
        _via0 = new ReadOnlyCollection<Vector2>[lanes.Count];
        _via1 = new ReadOnlyCollection<Vector2>[lanes.Count];
        for (int k = 0; k < lanes.Count; k++)
        {
            BotLane lane = lanes[k] ?? throw new ArgumentException($"Lane {k} is null.", nameof(lanes));
            if (string.IsNullOrEmpty(lane.Name)) throw new ArgumentException($"Lane {k} has no name.", nameof(lanes));
            if (lane.Via is null || lane.Via.Count < MinVia || lane.Via.Count > MaxVia)
                throw new ArgumentException($"Lane \"{lane.Name}\" needs {MinVia}–{MaxVia} via points, has {lane.Via?.Count ?? 0}.", nameof(lanes));
            Vector2[] via = CopyFinite(lane.Via, $"lane \"{lane.Name}\"'s via point", nameof(lanes));
            Vector2[] back = (Vector2[])via.Clone();
            Array.Reverse(back);
            _via0[k] = Array.AsReadOnly(via);
            _via1[k] = Array.AsReadOnly(back);
            copies[k] = new BotLane(lane.Name, _via0[k]);
        }
        _lanes = Array.AsReadOnly(copies);
        _cover0 = Array.AsReadOnly(CopyCover(cover0, nameof(cover0)));
        _cover1 = Array.AsReadOnly(CopyCover(cover1, nameof(cover1)));
    }

    /// <summary>The lanes, via points in team 0's order.</summary>
    public IReadOnlyList<BotLane> Lanes => _lanes;

    /// <summary>No lanes and no cover points.</summary>
    public bool IsEmpty => _lanes.Count == 0 && _cover0.Count == 0;

    /// <summary>Team <paramref name="team"/>'s cover points in front of its base.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A team other than 0 or 1.</exception>
    public IReadOnlyList<Vector2> CoverOf(byte team) => team switch
    {
        0 => _cover0,
        1 => _cover1,
        _ => throw new ArgumentOutOfRangeException(nameof(team), team, "Only teams 0 and 1 have cover points."),
    };

    /// <summary>Lane <paramref name="lane"/>'s via points in the order team <paramref name="team"/> drives them: team 0 as given, team 1 reversed.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No such lane, or a team other than 0 or 1.</exception>
    public IReadOnlyList<Vector2> ViaFor(int lane, byte team)
    {
        if ((uint)lane >= (uint)_lanes.Count) throw new ArgumentOutOfRangeException(nameof(lane), lane, $"There are {_lanes.Count} lanes.");
        return team switch
        {
            0 => _via0[lane],
            1 => _via1[lane],
            _ => throw new ArgumentOutOfRangeException(nameof(team), team, "Only teams 0 and 1 drive the lanes."),
        };
    }

    /// <summary>
    /// The lane whose via points stray farthest from the line through the two bases' centres (ties: lower index): the
    /// flanking one. −1 without lanes.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="bases"/> is not two bases with distinct centres.</exception>
    public int FlankLane(IReadOnlyList<TeamBase> bases)
    {
        ArgumentNullException.ThrowIfNull(bases);
        if (bases.Count != 2) throw new ArgumentException($"Expected two bases, got {bases.Count}.", nameof(bases));
        Vector2 a = bases[0].Centre, along = bases[1].Centre - a;
        float length = along.Length();
        if (!(length > 0)) throw new ArgumentException("The two bases share a centre: there is no line between them.", nameof(bases));
        Vector2 unit = along / length;

        int best = -1;
        float farthest = -1;
        for (int k = 0; k < _lanes.Count; k++)
        {
            float stray = 0;
            for (int v = 0; v < _via0[k].Count; v++) stray = MathF.Max(stray, MathF.Abs(Cross(unit, _via0[k][v] - a)));
            if (stray > farthest) (best, farthest) = (k, stray);
        }
        return best;
    }

    private static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;

    private static Vector2[] CopyCover(IReadOnlyList<Vector2> cover, string paramName)
    {
        if (cover.Count != 0 && (cover.Count < MinCover || cover.Count > MaxCover))
            throw new ArgumentException($"Expected no cover points or {MinCover}–{MaxCover}, got {cover.Count}.", paramName);
        return CopyFinite(cover, "cover point", paramName);
    }

    private static Vector2[] CopyFinite(IReadOnlyList<Vector2> points, string what, string paramName)
    {
        var copy = new Vector2[points.Count];
        for (int k = 0; k < copy.Length; k++)
        {
            Vector2 p = points[k];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) throw new ArgumentException($"A {what} {p} is not finite.", paramName);
            copy[k] = p;
        }
        return copy;
    }
}
