using Tank.Core.World;
using Tank.Net.Protocol;

namespace Tank.Client.Bootstrap;

/// <summary>
/// Command-line switches (pass after "--"), used for quick testing and automated screenshots:
/// --name N  --host  --join IP[:PORT]  --mode coop|basebattle|deathmatch  --bots N  --autostart
/// --camera chase  --netgraph  --screenshot FILE  --screenshot-at SECONDS  --quit-after SECONDS
/// --no-update（不检查在线更新）  --update-now（更新就绪后不询问直接安装，用于测试）  --autodrive（试驾场：自动驾驶并开火，用于截图）
/// --camera-preset front|side|hill|spin（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
/// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）  --sniper（试驾场：以狙击镜视角启动，用于截图）
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

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        var o = new LaunchOptions();
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
                "--fx-shot" => o with { FxShotDelay = double.TryParse(Next(), System.Globalization.CultureInfo.InvariantCulture, out double f) ? f : 0.05 },
                "--screenshot" => o with { ScreenshotPath = Next() },
                "--screenshot-at" => o with { ScreenshotAt = double.TryParse(Next(), System.Globalization.CultureInfo.InvariantCulture, out double s) ? s : o.ScreenshotAt },
                "--quit-after" => o with { QuitAfter = double.TryParse(Next(), System.Globalization.CultureInfo.InvariantCulture, out double q) ? q : 0 },
                _ => o,
            };
        }
        return o;
    }
}
