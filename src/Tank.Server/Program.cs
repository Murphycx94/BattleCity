using System.Runtime.InteropServices;
using Tank.Core.World;
using Tank.Net.Server;
using Tank.Net.Transport;

namespace Tank.Server;

/// <summary>
/// Headless dedicated server.
/// Usage: Tank.Server [--port 27015] [--mode coop|basebattle|deathmatch] [--map coop-01] [--name "Room"] [--stage 1]
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        ServerOptions options;
        try
        {
            options = ParseArgs(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine("usage: Tank.Server [--port N] [--mode coop|basebattle|deathmatch] [--map NAME] [--name ROOM] [--stage N]");
            Console.Error.WriteLine($"maps: {string.Join(", ", MapLibrary.Names)}");
            return 2;
        }

        void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

        GameServer? server = null;
        using var transport = new LiteNetServerTransport(options.Port, new RoomInfoRelay(() => server!), options.MaxPlayers);
        server = new GameServer(transport, options, Log);
        try
        {
            transport.Start();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        using var runner = new ServerRunner(server, Log);
        runner.Start();
        Log($"'{options.RoomName}' listening on UDP {options.Port}: {server.Mode} on {server.MapName}. Ctrl+C to stop.");

        using var stop = new ManualResetEventSlim();
        // Ctrl+C in a terminal, SIGTERM from a service manager: both shut down cleanly.
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; stop.Set(); });
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stop.Set(); });
        while (!stop.Wait(TimeSpan.FromSeconds(30)))
            Log($"players {server.ConnectedCount}, tick avg {runner.AverageTickMs:F3} ms, max {runner.MaxTickMs:F3} ms");

        Log("shutting down");
        return 0;
    }

    private static ServerOptions ParseArgs(string[] args)
    {
        var options = new ServerOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--port":
                    options = options with { Port = int.TryParse(Value(), out int p) && p is > 0 and < 65536 ? p : throw new ArgumentException("bad port") };
                    break;
                case "--mode":
                    options = options with { Mode = Enum.TryParse(Value(), true, out GameMode m) && Enum.IsDefined(m) ? m : throw new ArgumentException("bad mode") };
                    break;
                case "--map":
                    string map = Value();
                    options = options with { MapName = MapLibrary.Names.Contains(map) ? map : throw new ArgumentException($"unknown map '{map}'") };
                    break;
                case "--name":
                    options = options with { RoomName = Value() };
                    break;
                case "--stage":
                    options = options with { Stage = int.TryParse(Value(), out int s) && s > 0 ? s : throw new ArgumentException("bad stage") };
                    break;
                default:
                    throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }
        return options;
    }
}
