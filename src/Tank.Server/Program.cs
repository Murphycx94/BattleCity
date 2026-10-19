using System.Globalization;
using System.Runtime.InteropServices;
using Tank.Core.World;
using Tank.Net.Battle;
using Tank.Net.Server;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Server;

/// <summary>
/// Headless dedicated server. A battle mode (stage 4) runs a <see cref="BattleServer"/> at 30 Hz; the old modes
/// still run the old <see cref="GameServer"/> until stage 4C removes them.
/// Usage: Tank.Server [--port 27015] [--mode teambattle|annihilation|ffa|coop|basebattle|deathmatch] [--bots N] [--name "Room"] [--map coop-01] [--stage 1]
/// With no --mode, a battle server in team battle mode. --bots goes with the battle modes, --map and --stage with the old ones; the last --mode wins.
/// </summary>
public static class Program
{
    private const string Usage = "usage: Tank.Server [--port N] [--mode teambattle|annihilation|ffa|coop|basebattle|deathmatch] [--bots N] [--name ROOM] [--map NAME] [--stage N]\n" +
        "  no --mode: a battle server in teambattle mode (the last --mode given wins); --bots goes with teambattle|annihilation|ffa, --map and --stage with coop|basebattle|deathmatch";

    public static int Main(string[] args)
    {
        Options options;
        try
        {
            options = ParseArgs(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine(Usage);
            Console.Error.WriteLine($"old maps: {string.Join(", ", MapLibrary.Names)}");
            return 2;
        }

        void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        return options.Battle is { } battle ? RunBattle(battle, Log) : RunOld(options.Old, Log);
    }

    private static int RunBattle(BattleServerOptions options, Action<string> log)
    {
        BattleServer? server = null;
        using var transport = new LiteNetServerTransport(options.Port, null, BattleProtocol.MaxPlayers, BattleProtocol.ConnectKey, w => server!.WriteRoomInfo(w));
        server = new BattleServer(transport, options, log);
        if (!StartTransport(transport)) return 1;

        using var runner = new ServerRunner(server, log, SimConstants.TickRate);
        runner.Start();
        log($"'{options.RoomName}' listening on UDP {options.Port}: {ModeRules.Id(options.Mode)}, {options.Bots} bots. Ctrl+C to stop.");
        WaitForStop(() => log($"players {server.ConnectedCount}, {(server.MatchRunning ? "in a match" : "in the room")}, tick avg {runner.AverageTickMs:F3} ms, max {runner.MaxTickMs:F3} ms"));
        log("shutting down");
        return 0;
    }

    private static int RunOld(ServerOptions options, Action<string> log)
    {
        GameServer? server = null;
        using var transport = new LiteNetServerTransport(options.Port, new RoomInfoRelay(() => server!), options.MaxPlayers);
        server = new GameServer(transport, options, log);
        if (!StartTransport(transport)) return 1;

        using var runner = new ServerRunner(server, log);
        runner.Start();
        log($"'{options.RoomName}' listening on UDP {options.Port}: {server.Mode} on {server.MapName}. Ctrl+C to stop.");
        WaitForStop(() => log($"players {server.ConnectedCount}, tick avg {runner.AverageTickMs:F3} ms, max {runner.MaxTickMs:F3} ms"));
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

    /// <summary>Either a battle (stage 4) or an old game; the common options are kept in both.</summary>
    private sealed record Options(ServerOptions Old, BattleServerOptions? Battle);

    private static Options ParseArgs(string[] args)
    {
        var old = new ServerOptions();
        var battle = new BattleServerOptions();
        bool isBattle = true;   // no --mode: the battle server in team battle; the last --mode given wins
        bool botsGiven = false, mapGiven = false, stageGiven = false;
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--port":
                    int port = TryParseInt(Value(), out int p) && p is > 0 and < 65536 ? p : throw new ArgumentException("bad port");
                    old = old with { Port = port };
                    battle = battle with { Port = port };
                    break;
                case "--mode":
                    string mode = Value();
                    if (ModeRules.TryParse(mode, out BattleMode bm))
                    {
                        battle = battle with { Mode = bm };
                        isBattle = true;
                    }
                    else if (Enum.TryParse(mode, true, out GameMode gm) && Enum.IsDefined(gm))
                    {
                        old = old with { Mode = gm };
                        isBattle = false;
                    }
                    else throw new ArgumentException($"unknown mode '{mode}'");
                    break;
                case "--bots":
                    battle = battle with { Bots = TryParseInt(Value(), out int bots) && bots < BattleProtocol.MaxPlayers ? bots : throw new ArgumentException($"bots: 0 to {BattleProtocol.MaxPlayers - 1}") };
                    botsGiven = true;
                    break;
                case "--map":
                    string map = Value();
                    old = old with { MapName = MapLibrary.Names.Contains(map) ? map : throw new ArgumentException($"unknown map '{map}'") };
                    mapGiven = true;
                    break;
                case "--name":
                    string name = Value();
                    old = old with { RoomName = name };
                    battle = battle with { RoomName = name };
                    break;
                case "--stage":
                    old = old with { Stage = TryParseInt(Value(), out int s) && s > 0 ? s : throw new ArgumentException("bad stage") };
                    stageGiven = true;
                    break;
                default:
                    throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }
        if (isBattle && mapGiven) throw new ArgumentException("--map applies only to the old modes (coop, basebattle, deathmatch)");
        if (isBattle && stageGiven) throw new ArgumentException("--stage applies only to the old modes (coop, basebattle, deathmatch)");
        if (!isBattle && botsGiven) throw new ArgumentException("--bots applies only to the battle modes (teambattle, annihilation, ffa)");
        return new Options(old, isBattle ? battle : null);
    }

    /// <summary>A plain non-negative number: digits only, whatever the machine's culture.</summary>
    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
