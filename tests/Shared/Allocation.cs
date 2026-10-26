namespace Tank.Testing;

/// <summary>
/// Zero-allocation checks that a moment of runtime noise doesn't fail. Now and then (about one full Sim run in six) one
/// measured window reads a few KB on <see cref="GC.GetAllocatedBytesForCurrentThread"/> that the code under test never
/// asked for: it comes and goes with test order and timing (with tiered JIT and PGO off too; where it was caught, a gen-0
/// collection fell inside the window), where a real steady-state allocation shows in every run. So <see cref="AssertNone"/> builds the whole scenario afresh and measures it up to
/// <see cref="Attempts"/> times and passes on the first clean window; a real allocation fails all of them. Linked into
/// each test project that measures allocation.
/// </summary>
internal static class Allocation
{
    public const int Attempts = 3;

    /// <summary>What one window measured: the bytes this thread allocated in it, and the gen-0 collections either side.</summary>
    public readonly record struct Measured(long Bytes, int Gen0Before, int Gen0After);

    /// <summary>An open window: <see cref="Stop"/> it right after the measured work. Neither end allocates.</summary>
    public readonly struct Window
    {
        private readonly long _bytes;
        private readonly int _gen0;

        internal Window(long bytes, int gen0) => (_bytes, _gen0) = (bytes, gen0);

        public Measured Stop()
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            return new Measured(bytes - _bytes, _gen0, GC.CollectionCount(0));
        }
    }

    /// <summary>Opens the measured window: call it right after the warm-up.</summary>
    public static Window Start()
    {
        int gen0 = GC.CollectionCount(0);
        return new Window(GC.GetAllocatedBytesForCurrentThread(), gen0);
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> — the test's whole arrangement, its warm-up, then its measured work between
    /// <see cref="Start"/> and <see cref="Window.Stop"/> — up to <see cref="Attempts"/> times, each from scratch, and
    /// passes as soon as one window allocates nothing. Otherwise fails listing every attempt. Checks after the window
    /// belong inside <paramref name="attempt"/>, after <see cref="Window.Stop"/>: they hold for whichever attempt passed.
    /// </summary>
    public static void AssertNone(Func<Measured> attempt)
    {
        var tries = new Measured[Attempts];
        for (int i = 0; i < Attempts; i++)
        {
            tries[i] = attempt();
            if (tries[i].Bytes == 0) return;
        }
        Xunit.Assert.Fail($"Allocated in every one of {Attempts} attempts: " +
                          string.Join("; ", tries.Select((t, i) => $"#{i + 1} {t.Bytes} bytes, gen-0 collections {t.Gen0Before} → {t.Gen0After}")));
    }
}
