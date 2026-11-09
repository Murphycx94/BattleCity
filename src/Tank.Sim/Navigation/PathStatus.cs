namespace Tank.Sim.Navigation;

/// <summary>How a path request stands. Values are not on the wire.</summary>
public enum PathStatus : byte
{
    /// <summary>No search asked for (or its result taken).</summary>
    Idle,

    /// <summary>Searching: not finished yet.</summary>
    Pending,

    /// <summary>A path was found.</summary>
    Found,

    /// <summary>No path: an end has no open cell near it, the goal can't be reached, or the search gave up.</summary>
    NoPath,
}
