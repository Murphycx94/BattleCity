using System.Globalization;

namespace Tank.Soak;

/// <summary>Network soak test: runs <see cref="BattleSoak"/> and exits non-zero if any acceptance number is violated.</summary>
public static class Program
{
    /// <summary>
    /// Server tick p99 budget (2 ms on real hardware). Shared CI VMs add scheduler noise to wall-clock timing,
    /// so CI raises it via SOAK_TICK_BUDGET_MS to only catch gross regressions.
    /// </summary>
    private static readonly double TickBudgetMs =
        double.TryParse(Environment.GetEnvironmentVariable("SOAK_TICK_BUDGET_MS"), CultureInfo.InvariantCulture, out double b) ? b : 2.0;

    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            Console.Error.WriteLine($"error: unknown option '{args[0]}'");
            Console.Error.WriteLine("usage: Tank.Soak   (SOAK_TICK_BUDGET_MS overrides the 2 ms tick budget)");
            return 2;
        }
        return BattleSoak.Run(TickBudgetMs) ? 0 : 1;
    }
}
