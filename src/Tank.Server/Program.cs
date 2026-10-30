using System.Globalization;
using System.Runtime.InteropServices;
using Tank.Net.Battle;
using Tank.Net.Timing;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Server;

/// <summary>
/// Headless dedicated server: a room that runs <see cref="BattleServer"/> matches at 30 Hz.
/// Usage: Tank.Server [--port 27015] [--mode teambattle|annihilation|ffa|standard|royale] [--map sandbox|riverland] [--bots N] [--name "Room"]
/// With no --mode, team battle; with no --map, the riverland; the last of each given wins. A royale is played on the riverland
/// only (exit code 2 for --mode royale --map sandbox). The map is generated in the
/// background: the room takes players at once and starts a match once the map is ready. If that first map can't be
/// generated (a bug), the server says so and exits with code 3.
/// </summary>
public static class Program
{
    private const string Usage = "usage: Tank.Server [--port N] [--mode teambattle|annihilation|ffa|standard|royale] [--map sandbox|riverland] [--bots N] [--name ROOM]\n" +
        "  no --mode: teambattle; no --map: riverland (the last of each given wins); royale: riverland only;\n" +
        "  --bots: 0 to 15, added when a match starts (at most 9 outside a royale)";

    private const int MapFailedExitCode = 3;
    private static readonly TimeSpan StopPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(30);

    public static int Main(string[] args)
    {
        BattleServerOptions options;
        try
        {
            options = ParseArgs(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine(Usage);
            return 2;
        }

        void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        return Run(options, Log);
    }

    private static int Run(BattleServerOptions options, Action<string> log)
    {
        BattleServer? server = null;
        using var transport = new LiteNetServerTransport(options.Port, BattleProtocol.MaxPlayers, BattleProtocol.ConnectKey, w => server!.WriteRoomInfo(w));
        server = new BattleServer(transport, options, log);
        if (!StartTransport(transport)) return 1;

        using var runner = new ServerRunner(server, log, SimConstants.TickRate);
        runner.Start();
        log($"'{options.RoomName}' listening on UDP {options.Port}: {ModeRules.Id(options.Mode)}, map {options.Map.Id}, {server.Bots} bots. Ctrl+C to stop.");
        bool stopped = WaitForStop(
            () => log($"players {server.ConnectedCount}, {(server.MatchRunning ? "in a match" : "in the room")}, tick avg {runner.AverageTickMs:F3} ms, max {runner.MaxTickMs:F3} ms"),
            () => server.MapStateOf(options.Map) == MapState.Failed);
        if (!stopped)
        {
            Console.Error.WriteLine($"error: the map '{options.Map.Id}' failed to generate (see the log above); no match could ever start. Exiting.");
            return MapFailedExitCode;
        }
        log("shutting down");
        return 0;
    }

    private static bool StartTransport(LiteNetServerTransport transport)
    {
        try
        {
            transport.Start();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Blocks until Ctrl+C in a terminal or SIGTERM from a service manager (true), or until <paramref name="failed"/>
    /// (false), reporting every 30 s.
    /// </summary>
    private static bool WaitForStop(Action report, Func<bool> failed)
    {
        using var stop = new ManualResetEventSlim();
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; stop.Set(); });
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stop.Set(); });
        var sinceReport = System.Diagnostics.Stopwatch.StartNew();
        while (!stop.Wait(StopPollInterval))
        {
            if (failed()) return false;
            if (sinceReport.Elapsed < ReportInterval) continue;
            report();
            sinceReport.Restart();
        }
        return true;
    }

    private static BattleServerOptions ParseArgs(string[] args)
    {
        var options = new BattleServerOptions { Map = MapCatalog.Riverland, PrepareMapsInBackground = true };
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--port":
                    options = options with { Port = TryParseInt(Value(), out int p) && p is > 0 and < 65536 ? p : throw new ArgumentException("bad port") };
                    break;
                case "--mode":
                    string mode = Value();
                    options = options with { Mode = ModeRules.TryParse(mode, out BattleMode m) ? m : throw new ArgumentException($"unknown mode '{mode}'") };
                    break;
                case "--map":
                    string map = Value();
                    options = options with { Map = MapCatalog.TryGet(map, out MapDefinition? found) ? found : throw new ArgumentException($"unknown map '{map}'") };
                    break;
                case "--bots":
                    options = options with { Bots = TryParseInt(Value(), out int bots) && bots < BattleProtocol.MaxPlayers ? bots : throw new ArgumentException($"bots: 0 to {BattleProtocol.MaxPlayers - 1}") };
                    break;
                case "--name":
                    options = options with { RoomName = Value() };
                    break;
                default:
                    throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }
        if (!ModeRules.AllowsMap(options.Mode, options.Map.Id))
            throw new ArgumentException($"--mode {ModeRules.Id(options.Mode)} is played on the {ModeRules.OnlyMap(options.Mode)} only (--map {ModeRules.OnlyMap(options.Mode)})");
        return options;
    }

    /// <summary>A plain non-negative number: digits only, whatever the machine's culture.</summary>
    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
