using System.Globalization;
using Tank.Net.Battle;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Client.Bootstrap;

/// <summary>
/// Command-line switches (pass after "--"), used for quick testing and automated screenshots:
/// --name N  --host  --join IP[:PORT]  --port N  --mode teambattle|annihilation|ffa|standard|royale  --bots N（最多 15）  --autostart  --players N（房主等够 N 个真人才自动开局，N 大于 1 时人齐后再等约半秒；默认 1，最多一个房间的人数 16）
/// --seed N（对局种子，可复现）  --match-seconds S（对局时长，覆盖模式默认值）  --loopback（房主用虚拟网络和固定步长，没有套接字和线程，用于确定性冒烟）
/// --sandbox（启动后直接进试驾场）  --hud-demo NAME（对局 HUD 画廊：用预制帧在纯色底上画 HUD 的每种状态，用于验收排版）
/// --netgraph（F3 网络面板一开始就打开）  --screenshot FILE（一次运行只截一张）  --screenshot-at SECONDS（还没进过对局时从启动起算，在菜单、房间或 HUD 画廊里截；
/// 一进对局就交给对局，从对局场景创建起算；--shot-after、--fx-shot 只由对局或试驾场截）  --quit-after SECONDS
/// --no-update（不检查在线更新）  --update-now（更新就绪后不询问直接安装，用于测试）  --autodrive（试驾场和对局：自动驾驶并开火，用于截图）
/// --map sandbox|riverland（开房时房间的地图，默认 riverland，吃鸡只在河谷开、--mode royale 时另给的图警告并用河谷；试驾场用哪张地图，默认 sandbox；写错时警告并用默认的）
/// --camera-preset front|side|close|closerear|sprocket|spin|orbit|target，试驾场地图另有 hill|bush|yard|gate|patrol，河谷另有 overview|town|ford|forest|plateau|dunes|map，两张图都有 base0|base1|base0near|base1near|base0far|base1far，吃鸡对局另有 zone（光墙近处）|crate（离自己最近的已知箱子特写）（试驾场和对局：固定镜头，用于截图和测帧）  --fps-log（试驾场和对局：每秒打印平均和最差帧时间）
/// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）  --sniper（试驾场和对局：以狙击镜视角启动，用于截图）
/// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn|wall|breach|fence|ram|bush|lamp，河谷上是 house|ram-house|trees|ford
/// （试驾场：自动对靶车开炮、开车撞墙，河谷上打塌、撞塌房子、开进树林、涉过浅滩，用于截图）
/// --shot-after fire|hit|kill|prop|down|death|ended|outside|pickup:SECONDS（玩家第一次开炮、第一次命中坦克、第一次击毁坦克、第一次有墙受伤（prop，仅试驾场）、第一次有物件被毁（down，仅试驾场）、
/// 自己的坦克第一次被击毁（death，仅对局）、对局结算开始（ended，仅对局）、自己的坦克第一次在安全区外（outside，仅吃鸡对局）或自己第一次吃到补给箱（pickup，仅吃鸡对局）后再过 SECONDS 秒截图）
/// --tank light|medium|heavy|td（开局的车型：试驾场里默认中坦；房间里则在连上后自动选这辆）  --spot-debug（试驾场：一开始就显示点亮调试文字，同 V 键）
/// --full-map（进场后打开全屏地图，截图用）
/// A plain parser, free of the engine: values it can't read fall back to defaults and are listed in <see cref="Warnings"/>
/// for the caller to report, and so are a switch it doesn't know and a switch that needs a value but is followed by
/// another switch (or by nothing), which is then left as it was. This is the only place the client reads its arguments.
/// </summary>
public sealed record LaunchOptions
{
    private static readonly string[] ShotMoments = { "fire", "hit", "kill", "prop", "down", "death", "ended", "outside", "pickup" };
    private const int MaxBots = BattleProtocol.MaxPlayers - 1;

    public string? Name { get; init; }
    public bool Host { get; init; }
    public string? JoinAddress { get; init; }
    public int Port { get; init; } = BattleProtocol.DefaultPort;

    /// <summary>The mode to host (and the host's auto-room to set); null leaves the server's default.</summary>
    public BattleMode? Mode { get; init; }
    public int Bots { get; init; }
    public bool AutoStart { get; init; }

    /// <summary>How many connected people the host waits for before starting by itself (<c>--players</c>); at least 1.</summary>
    public int Players { get; init; } = 1;

    /// <summary>A fixed match seed (<c>--seed</c>); null for a random one per match.</summary>
    public uint? Seed { get; init; }

    /// <summary>Host on a virtual network with fixed steps (<c>--loopback</c>): no sockets, no threads.</summary>
    public bool Loopback { get; init; }

    /// <summary>Start straight in the sandbox (<c>--sandbox</c>).</summary>
    public bool Sandbox { get; init; }

    /// <summary>The <see cref="MapCatalog"/> id of the map to use (<c>--map</c>), in the catalog's spelling; null when not given or not a known map.</summary>
    public string? Map { get; init; }

    /// <summary>The room's first map when hosting: the mode's only map when it has one (a royale: the riverland), else <see cref="Map"/>, else the riverland (stage 5 design §2).</summary>
    public string HostMap => (Mode is { } mode ? ModeRules.OnlyMap(mode) : null) ?? Map ?? MapCatalog.Riverland.Id;

    /// <summary>A match length in seconds that replaces the mode's (<c>--match-seconds</c>); null for the mode's own.</summary>
    public double? MatchSeconds { get; init; }

    /// <summary>The HUD gallery to show instead of a match (<c>--hud-demo</c>); null when not given.</summary>
    public string? HudDemo { get; init; }

    /// <summary>Whether the F3 network panel starts open (<c>--netgraph</c>).</summary>
    public bool NetGraph { get; init; }
    public string? ScreenshotPath { get; init; }
    public double ScreenshotAt { get; init; } = 6;
    public double QuitAfter { get; init; }
    public bool NoUpdate { get; init; }
    public bool UpdateNow { get; init; }
    public bool AutoDrive { get; init; }
    public string? CameraPreset { get; init; }
    public bool FpsLog { get; init; }
    public double? FxShotDelay { get; init; }
    public bool Sniper { get; init; }
    public bool SpotDebug { get; init; }

    /// <summary>Open the full-screen map once the scene is up (<c>--full-map</c>), for screenshots.</summary>
    public bool FullMap { get; init; }
    public string? RangeDemo { get; init; }

    /// <summary>"fire", "hit", "kill", "prop", "down", "death", "ended", "outside" or "pickup" (from <c>--shot-after EVENT:SECONDS</c>); null when not given.</summary>
    public string? ShotAfterEvent { get; init; }
    public double ShotAfterDelay { get; init; }

    /// <summary>The <c>TankCatalog</c> id to start in (<c>--tank</c>); null when not given or not a known tank.</summary>
    public string? Tank { get; init; }

    /// <summary>One line per option value that couldn't be read (and was ignored or replaced by its default).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        var o = new LaunchOptions();
        var warnings = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            bool missing = false;
            string Next()
            {
                if (i + 1 < args.Count && !IsSwitch(args[i + 1])) return args[++i];
                missing = true;   // "--screenshot --screenshot-at 20" must not take "--screenshot-at" for the file
                return "";
            }
            int warned = warnings.Count;
            LaunchOptions parsed = arg switch
            {
                "--name" => o with { Name = Next() },
                "--host" => o with { Host = true },
                "--join" => o with { JoinAddress = Next() },
                "--port" => o with { Port = ParsePort(Next(), o.Port, warnings) },
                "--mode" => ParseMode(o, Next(), warnings),
                "--bots" => o with { Bots = ParseBots(Next(), warnings) },
                "--players" => o with { Players = ParsePlayers(Next(), o.Players, warnings) },
                "--seed" => ParseSeed(o, Next(), warnings),
                "--match-seconds" => ParseMatchSeconds(o, Next(), warnings),
                "--autostart" => o with { AutoStart = true },
                "--loopback" => o with { Loopback = true },
                "--sandbox" => o with { Sandbox = true },
                "--map" => ParseMap(o, Next(), warnings),
                "--hud-demo" => o with { HudDemo = Next() },
                "--netgraph" => o with { NetGraph = true },
                "--no-update" => o with { NoUpdate = true },
                "--update-now" => o with { UpdateNow = true },
                "--autodrive" => o with { AutoDrive = true },
                "--camera-preset" => o with { CameraPreset = Next() },
                "--fps-log" => o with { FpsLog = true },
                "--sniper" => o with { Sniper = true },
                "--spot-debug" => o with { SpotDebug = true },
                "--full-map" => o with { FullMap = true },
                "--fx-shot" => o with { FxShotDelay = Seconds("--fx-shot", Next(), 0.05, warnings) },
                "--range-demo" => o with { RangeDemo = Next() },
                "--shot-after" => ParseShotAfter(o, Next(), warnings),
                "--tank" => ParseTank(o, Next(), warnings),
                "--screenshot" => o with { ScreenshotPath = Next() },
                "--screenshot-at" => o with { ScreenshotAt = Seconds("--screenshot-at", Next(), o.ScreenshotAt, warnings) },
                "--quit-after" => o with { QuitAfter = Seconds("--quit-after", Next(), 0, warnings) },
                _ => Unknown(o, arg, warnings),
            };
            if (missing)
            {
                // Whatever the switch made of the empty value, and said about it, gives way to one plain warning.
                warnings.RemoveRange(warned, warnings.Count - warned);
                warnings.Add(i + 1 < args.Count
                    ? $"{arg} needs a value but is followed by {args[i + 1]}; {arg} is ignored."
                    : $"{arg} needs a value but is the last argument; it is ignored.");
                continue;
            }
            o = parsed;
        }
        // Hosting a mode with a map of its own on another map: the room opens on the mode's (the client doesn't quit over an argument), with a word.
        if (o.Host && o.Mode is { } hostMode && o.Map is { } map && !ModeRules.AllowsMap(hostMode, map))
            warnings.Add($"--mode {ModeRules.Id(hostMode)} is played on the {ModeRules.OnlyMap(hostMode)} only; hosting on the {ModeRules.OnlyMap(hostMode)}.");
        return o with { Warnings = warnings };
    }

    /// <summary>"--x", not a bare "--": the dev scripts' "-- args…" passes the separator itself through, and it means nothing here.</summary>
    private static bool IsSwitch(string arg) => arg.Length > 2 && arg.StartsWith("--", StringComparison.Ordinal);

    /// <summary>A switch this parser doesn't know (an old one like <c>--camera</c>, or a typo) is ignored with a warning; a stray value is just ignored.</summary>
    private static LaunchOptions Unknown(LaunchOptions o, string arg, List<string> warnings)
    {
        if (IsSwitch(arg)) warnings.Add($"Unknown option {arg}; it is ignored. The options are listed in LaunchOptions.cs.");
        return o;
    }

    /// <summary><paramref name="value"/> as seconds, or <paramref name="fallback"/> (noting a warning) when it isn't a number.</summary>
    private static double Seconds(string option, string value, double fallback, List<string> warnings)
    {
        if (TryParseSeconds(value, out double seconds)) return seconds;
        warnings.Add($"Bad {option} '{value}'; use SECONDS, e.g. 2.5. Using {fallback.ToString(CultureInfo.InvariantCulture)}.");
        return fallback;
    }

    private static LaunchOptions ParseMode(LaunchOptions o, string value, List<string> warnings)
    {
        if (ModeRules.TryParse(value, out BattleMode mode)) return o with { Mode = mode };
        warnings.Add($"Bad --mode '{value}'; use {string.Join("|", ModeRules.All.Select(ModeRules.Id))}. Using the server's default.");
        return o with { Mode = null };
    }

    private static int ParseBots(string value, List<string> warnings)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bots))
        {
            warnings.Add($"Bad --bots '{value}'; use a number from 0 to {MaxBots}. Using 0.");
            return 0;
        }
        int clamped = Math.Clamp(bots, 0, MaxBots);
        if (clamped != bots) warnings.Add($"--bots {bots} is out of range; using {clamped} (0 to {MaxBots}).");
        return clamped;
    }

    private static int ParsePort(string value, int fallback, List<string> warnings)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port is >= 1 and <= ushort.MaxValue)
            return port;
        warnings.Add($"Bad --port '{value}'; use 1 to {ushort.MaxValue}. Using {fallback.ToString(CultureInfo.InvariantCulture)}.");
        return fallback;
    }

    private static int ParsePlayers(string value, int fallback, List<string> warnings)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int players) || players < 1)
        {
            warnings.Add($"Bad --players '{value}'; use a whole number of at least 1. Using {fallback.ToString(CultureInfo.InvariantCulture)}.");
            return fallback;
        }
        if (players <= BattleProtocol.MaxPlayers) return players;
        // More could never be in the room: the host would wait for ever.
        warnings.Add($"--players {players} is more than a room holds; using {BattleProtocol.MaxPlayers}.");
        return BattleProtocol.MaxPlayers;
    }

    private static LaunchOptions ParseSeed(LaunchOptions o, string value, List<string> warnings)
    {
        if (uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint seed)) return o with { Seed = seed };
        warnings.Add($"Bad --seed '{value}'; use a whole number from 0 to {uint.MaxValue}. Using a random seed.");
        return o;
    }

    private static LaunchOptions ParseMatchSeconds(LaunchOptions o, string value, List<string> warnings)
    {
        // The server refuses anything outside 0..MaxMatchSeconds, so a value it would throw on is rejected here.
        if (TryParseSeconds(value, out double seconds) && seconds > 0 && seconds <= BattleServerOptions.MaxMatchSeconds)
            return o with { MatchSeconds = seconds };
        warnings.Add($"Bad --match-seconds '{value}'; use SECONDS above 0, e.g. 30. Using the mode's own length.");
        return o;
    }

    private static LaunchOptions ParseMap(LaunchOptions o, string value, List<string> warnings)
    {
        if (MapCatalog.TryGet(value, out MapDefinition? map)) return o with { Map = map.Id };
        // Map stays null: the sandbox then uses the sandbox map.
        warnings.Add($"Bad --map '{value}'; use {string.Join("|", MapCatalog.All.Select(m => m.Id))}. Using the default map.");
        return o;
    }

    private static LaunchOptions ParseShotAfter(LaunchOptions o, string value, List<string> warnings)
    {
        string[] parts = value.Split(':');
        if (parts.Length != 2 || !ShotMoments.Contains(parts[0]) || !TryParseSeconds(parts[1], out double delay))
        {
            warnings.Add($"Bad --shot-after '{value}'; use {string.Join("|", ShotMoments)}:SECONDS, e.g. kill:2.5.");
            return o;
        }
        return o with { ShotAfterEvent = parts[0], ShotAfterDelay = delay };
    }

    private static LaunchOptions ParseTank(LaunchOptions o, string value, List<string> warnings)
    {
        if (TankCatalog.All.Any(spec => spec.Id == value)) return o with { Tank = value };
        // Tank stays null: SandboxMain applies the default (the medium tank) when no valid --tank was given.
        warnings.Add($"Bad --tank '{value}'; use {string.Join("|", TankCatalog.All.Select(spec => spec.Id))}. Using medium.");
        return o;
    }

    private static bool TryParseSeconds(string value, out double seconds) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && double.IsFinite(seconds);
}
