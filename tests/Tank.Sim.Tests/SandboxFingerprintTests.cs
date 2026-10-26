using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

/// <summary>
/// Pins the sandbox map by its <see cref="MapFingerprint"/> (quantised, so it holds on Apple silicon and Intel alike): the
/// riverland work (stage 5A) changes what a battlefield can hold, and the sandbox must come out of it as it was
/// (riverland design §4.1).
/// </summary>
public sealed class SandboxFingerprintTests
{
    [Fact]
    public void The_sandbox_map_is_what_it_was()
    {
        Battlefield field = SandboxMap.Create();

        Assert.Equal(0x8D8C3799D6F7A68AUL, MapFingerprint.Of(field));
        Assert.Equal(351, field.Props.Count);
        Assert.Equal(88, field.Props.Count(p => p.Kind == PropKind.Rock));
        Assert.Equal(220, field.Props.Count(p => p.Kind == PropKind.Bush));
        Assert.Equal(34, field.Props.Count(p => p.IsWall));
        Assert.Equal(9, field.Props.Count(p => p.Kind == PropKind.Fence));
    }
}
