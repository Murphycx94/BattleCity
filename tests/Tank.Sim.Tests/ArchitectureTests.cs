using System.Reflection;

namespace Tank.Sim.Tests;

/// <summary>
/// Tank.Sim must stay pure — no engine, no network, no wall clock, no unseeded randomness — so the client's prediction
/// replays exactly what the server simulated and every test is reproducible.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Sim = typeof(SimConstants).Assembly;

    [Fact]
    public void Sim_references_only_the_base_library()
    {
        string[] refs = Sim.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        Assert.All(refs, name => Assert.True(name.StartsWith("System") || name == "netstandard" || name == "mscorlib", $"Tank.Sim must not reference {name}"));
    }

    [Fact]
    public void Sim_keeps_no_wall_clock_or_system_random()
    {
        var forbidden = new[] { typeof(Random), typeof(DateTime), typeof(System.Diagnostics.Stopwatch) };
        string[] offenders = Sim.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(f => forbidden.Contains(f.FieldType))
            .Select(f => $"{f.DeclaringType!.Name}.{f.Name}")
            .ToArray();
        Assert.Empty(offenders);
    }
}
