using Tank.Core.World;
using Tank.Net.Protocol;

namespace Tank.Client.Bootstrap;

/// <summary>
/// Command-line switches (pass after "--"), used for quick testing and automated screenshots:
/// --name N  --host  --join IP[:PORT]  --mode coop|basebattle|deathmatch  --bots N  --autostart
/// --camera chase  --netgraph  --screenshot FILE  --screenshot-at SECONDS  --quit-after SECONDS
/// --no-update（不检查在线更新）  --update-now（更新就绪后不询问直接安装，用于测试）
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
                "--screenshot" => o with { ScreenshotPath = Next() },
                "--screenshot-at" => o with { ScreenshotAt = double.TryParse(Next(), System.Globalization.CultureInfo.InvariantCulture, out double s) ? s : o.ScreenshotAt },
                "--quit-after" => o with { QuitAfter = double.TryParse(Next(), System.Globalization.CultureInfo.InvariantCulture, out double q) ? q : 0 },
                _ => o,
            };
        }
        return o;
    }
}
