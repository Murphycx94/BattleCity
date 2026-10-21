using System.Globalization;
using System.Runtime.InteropServices;
using Tank.Net.Battle;
using Tank.Net.Timing;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Server;

/// <summary>
/// Headless dedicated server: a room that runs <see cref="BattleServer"/> matches at 30 Hz.
/// Usage: Tank.Server [--port 27015] [--mode teambattle|annihilation|ffa] [--bots N] [--name "Room"]
/// With no --mode, team battle; the last --mode given wins.
/// </summary>
public static class Program
{
    private const string Usage = "usage: Tank.Server [--port N] [--mode teambattle|annihilation|ffa] [--bots N] [--name ROOM]\n" +
        "  no --mode: teambattle (the last --mode given wins); --bots: 0 to 9, added when a match starts";

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
        log($"'{options.RoomName}' listening on UDP {options.Port}: {ModeRules.Id(options.Mode)}, {options.Bots} bots. Ctrl+C to stop.");
        WaitForStop(() => log($"players {server.ConnectedCount}, {(server.MatchRunning ? "in a match" : "in the room")}, tick avg {runner.AverageTickMs:F3} ms, max {runner.MaxTickMs:F3} ms"));
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

    /// <summary>Blocks until Ctrl+C in a terminal or SIGTERM from a service manager, reporting every 30 s.</summary>
    private static void WaitForStop(Action report)
    {
        using var stop = new ManualResetEventSlim();
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; stop.Set(); });
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stop.Set(); });
        while (!stop.Wait(TimeSpan.FromSeconds(30))) report();
    }

    private static BattleServerOptions ParseArgs(string[] args)
    {
        var options = new BattleServerOptions();
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
        return options;
    }

    /// <summary>A plain non-negative number: digits only, whatever the machine's culture.</summary>
    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
