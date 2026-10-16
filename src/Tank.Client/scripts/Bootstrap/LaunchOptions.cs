using System.Globalization;
using Tank.Core.World;
using Tank.Net.Protocol;
using Tank.Sim.Vehicles;

namespace Tank.Client.Bootstrap;

/// <summary>
/// Command-line switches (pass after "--"), used for quick testing and automated screenshots:
/// --name N  --host  --join IP[:PORT]  --mode coop|basebattle|deathmatch  --bots N  --autostart
/// --camera chase  --netgraph  --screenshot FILE  --screenshot-at SECONDS  --quit-after SECONDS
/// --no-update（不检查在线更新）  --update-now（更新就绪后不询问直接安装，用于测试）  --autodrive（试驾场：自动驾驶并开火，用于截图）
/// --camera-preset front|side|hill|spin（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
/// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）  --sniper（试驾场：以狙击镜视角启动，用于截图）
/// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn（试驾场：自动对靶车开炮，用于截图）  --shot-after fire|hit|kill:SECONDS（试驾场：玩家第一次开炮、第一次命中坦克或第一次击毁坦克后再过 SECONDS 秒截图）
/// --tank light|medium|heavy|td（试驾场：开局的车型，默认中坦）
/// A plain parser, free of the engine: values it can't read fall back to defaults and are listed in <see cref="Warnings"/>
/// for the caller to report.
/// </summary>
public sealed record LaunchOptions
{
    public string? Name { get; init; }
    public bool Host { get; init; }
    public string? JoinAddress { get; init; }
    public int Port { get; init; } = ProtocolInfo.DefaultPort;
    public GameMode Mode { get; init; } = GameMode.Coop;
    public int Bots { get; init; }
    public bool AutoStart { get; init; }
    public bool ChaseCamera { get; init; }
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
    public string? RangeDemo { get; init; }

    /// <summary>"fire", "hit" or "kill" (from <c>--shot-after EVENT:SECONDS</c>); null when not given.</summary>
    public string? ShotAfterEvent { get; init; }
    public double ShotAfterDelay { get; init; }

    /// <summary>The <c>TankCatalog</c> id to start the sandbox in (<c>--tank</c>); null when not given or not a known tank.</summary>
    public string? Tank { get; init; }

    /// <summary>One line per option value that couldn't be read (and was ignored or replaced by its default).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        var o = new LaunchOptions();
        var warnings = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            string Next() => i + 1 < args.Count ? args[++i] : "";
            o = args[i] switch
            {
                "--name" => o with { Name = Next() },
                "--host" => o with { Host = true },
                "--join" => o with { JoinAddress = Next() },
                "--port" => o with { Port = int.TryParse(Next(), out int p) ? p : o.Port },
                "--mode" => o with { Mode = Enum.TryParse(Next(), true, out GameMode m) ? m : o.Mode },
                "--bots" => o with { Bots = int.TryParse(Next(), out int b) ? Math.Clamp(b, 0, 7) : 0 },
                "--autostart" => o with { AutoStart = true },
                "--camera" => o with { ChaseCamera = Next() == "chase" },
                "--netgraph" => o with { NetGraph = true },
                "--no-update" => o with { NoUpdate = true },
                "--update-now" => o with { UpdateNow = true },
                "--autodrive" => o with { AutoDrive = true },
                "--camera-preset" => o with { CameraPreset = Next() },
                "--fps-log" => o with { FpsLog = true },
                "--sniper" => o with { Sniper = true },
                "--fx-shot" => o with { FxShotDelay = Seconds("--fx-shot", Next(), 0.05, warnings) },
                "--range-demo" => o with { RangeDemo = Next() },
                "--shot-after" => ParseShotAfter(o, Next(), warnings),
                "--tank" => ParseTank(o, Next(), warnings),
                "--screenshot" => o with { ScreenshotPath = Next() },
                "--screenshot-at" => o with { ScreenshotAt = Seconds("--screenshot-at", Next(), o.ScreenshotAt, warnings) },
                "--quit-after" => o with { QuitAfter = Seconds("--quit-after", Next(), 0, warnings) },
                _ => o,
            };
        }
        return o with { Warnings = warnings };
    }

    /// <summary><paramref name="value"/> as seconds, or <paramref name="fallback"/> (noting a warning) when it isn't a number.</summary>
    private static double Seconds(string option, string value, double fallback, List<string> warnings)
    {
        if (TryParseSeconds(value, out double seconds)) return seconds;
        warnings.Add($"Bad {option} '{value}'; use SECONDS, e.g. 2.5. Using {fallback.ToString(CultureInfo.InvariantCulture)}.");
        return fallback;
    }

    private static LaunchOptions ParseShotAfter(LaunchOptions o, string value, List<string> warnings)
    {
        string[] parts = value.Split(':');
        if (parts.Length != 2 || parts[0] is not ("fire" or "hit" or "kill") || !TryParseSeconds(parts[1], out double delay))
        {
            warnings.Add($"Bad --shot-after '{value}'; use fire|hit|kill:SECONDS, e.g. kill:2.5.");
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
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
}
