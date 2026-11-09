namespace Tank.Net.Tests;

/// <summary>The wall-clock tests: run on their own, after the parallel ones, so the other tests' threads don't time them.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}
