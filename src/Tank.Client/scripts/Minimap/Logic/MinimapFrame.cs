using System.Diagnostics;
using System.Numerics;
using Tank.Client.Match;

namespace Tank.Client.Minimap;

/// <summary>A tank's shape on the map, by its catalog index (<c>TankCatalog.All</c>'s order): drawn upright whatever its heading.</summary>
public enum MinimapShape : byte { Light, Medium, Heavy, Td }

/// <summary>One tank on the map: where it is on the ground (x, z), whose it is, its shape, and whether it is a wreck.</summary>
public readonly record struct MinimapTank(Vector2 Position, Side Side, MinimapShape Shape, bool Destroyed);

/// <summary>A base: <paramref name="Points"/> 0–100 is how far it has been taken (MatchBlock.BaseProgress of its team).</summary>
/// <param name="Centre">Ground (x, z).</param>
/// <param name="Radius">Metres.</param>
/// <param name="Own">Whether it is the player's team's base.</param>
public readonly record struct MinimapBase(Vector2 Centre, float Radius, bool Own, byte Points);

/// <summary>What marks where the player looks from: nothing, an arrow on a tank, or a ring on a point.</summary>
public enum EyeKind : byte { None, Arrow, Ring }

/// <summary>
/// Where the player looks from: an <see cref="EyeKind.Arrow"/> on a tank (the own, or the one followed) pointing along
/// <paramref name="Yaw"/>, or a <see cref="EyeKind.Ring"/> on the free camera's focus (or the own wreck's close-up); the
/// fan opens along <paramref name="CameraYaw"/> by ±<paramref name="HalfFov"/> (the camera's horizontal half-angle).
/// </summary>
/// <param name="Position">Ground (x, z).</param>
public readonly record struct MinimapEye(EyeKind Kind, Vector2 Position, float Yaw, float CameraYaw, float HalfFov);

/// <summary>One frame's icons, filled again every frame; nothing here allocates once built.</summary>
public sealed class MinimapFrame
{
    /// <summary>
    /// Headroom over what any scene has: a match at most <c>BattleProtocol.MaxPlayers</c> (10) tanks, the sandbox its
    /// player and a handful of targets (<c>SandboxMap.TargetSpots</c>, <c>RiverlandMap.TargetSpots</c>). One more is a bug
    /// (asserted in Debug) and is left off the map.
    /// </summary>
    public const int MaxTanks = 32;

    /// <summary>A standard match has two bases; other modes none.</summary>
    public const int MaxBases = 2;

    private readonly MinimapTank[] _tanks = new MinimapTank[MaxTanks];
    private readonly MinimapBase[] _bases = new MinimapBase[MaxBases];
    private int _tankCount, _baseCount;

    /// <summary>The tanks added since the last <see cref="Clear"/>, in the order they were added.</summary>
    public ReadOnlySpan<MinimapTank> Tanks => _tanks.AsSpan(0, _tankCount);

    /// <summary>The bases: 0 or 2.</summary>
    public ReadOnlySpan<MinimapBase> Bases => _bases.AsSpan(0, _baseCount);

    public MinimapEye Eye { get; set; }

    /// <summary>The own wreck while the eye is elsewhere (dead, watching): an orange cross. Null otherwise.</summary>
    public Vector2? OwnWreck { get; set; }

    /// <summary>Empties the tanks and bases, the eye (<see cref="EyeKind.None"/>) and the own wreck.</summary>
    public void Clear()
    {
        _tankCount = 0;
        _baseCount = 0;
        Eye = default;
        OwnWreck = null;
    }

    /// <summary>Adds <paramref name="tank"/>; ignored once <see cref="MaxTanks"/> are in (a Debug assertion says so).</summary>
    public void AddTank(in MinimapTank tank)
    {
        Debug.Assert(_tankCount < MaxTanks, $"More than {MaxTanks} tanks on the map: the one at {tank.Position} is dropped.");
        if (_tankCount < MaxTanks) _tanks[_tankCount++] = tank;
    }

    /// <summary>Adds <paramref name="b"/>; ignored once <see cref="MaxBases"/> are in.</summary>
    public void AddBase(in MinimapBase b)
    {
        if (_baseCount < MaxBases) _bases[_baseCount++] = b;
    }
}
