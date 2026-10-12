using System.Reflection;

namespace Tank.Core.Tests;

/// <summary>Guards docs/04 §1: the simulation must stay pure so prediction and replays stay exact.</summary>
public sealed class ArchitectureTests
{
    [Fact]
    public void Core_ReferencesOnlyTheBaseLibrary()
    {
        var refs = typeof(Simulation).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        Assert.All(refs, name => Assert.True(name.StartsWith("System") || name == "netstandard" || name == "mscorlib", $"Tank.Core must not reference {name}"));
    }

    [Fact]
    public void Core_DoesNotUseWallClockOrSystemRandom()
    {
        var forbidden = new[] { typeof(Random), typeof(DateTime), typeof(System.Diagnostics.Stopwatch) };
        var offenders = typeof(Simulation).Assembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(f => forbidden.Contains(f.FieldType))
            .Select(f => $"{f.DeclaringType!.Name}.{f.Name}")
            .ToArray();
        Assert.Empty(offenders);
    }
}
