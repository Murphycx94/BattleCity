namespace Tank.Client.Sandbox;

/// <summary>
/// Collects per-frame render costs and reports average and worst once per second (<c>--fps-log</c>). Wall-clock frame time
/// is meaningless under the 60 fps cap, so CPU render, GPU render and process time (all ms) are tracked separately,
/// together with the latest frame's draw calls and primitives. The first (startup) window is dropped.
/// </summary>
public sealed class FrameTimeLog
{
    private const double Window = 1.0;
    private double _elapsed;
    private int _frames;
    private bool _startupDropped;
    private readonly Stat _cpu = new(), _gpu = new(), _process = new();

    private sealed class Stat
    {
        public double Sum, Worst;
        public void Add(double v) { Sum += v; Worst = Math.Max(Worst, v); }
        public void Reset() => Sum = Worst = 0;
    }

    /// <returns>A report line when a full second has been collected, otherwise null.</returns>
    public string? Add(double frameSeconds, double cpuMs, double gpuMs, double processMs, long drawCalls, long primitives)
    {
        _elapsed += frameSeconds;
        _frames++;
        _cpu.Add(cpuMs);
        _gpu.Add(gpuMs);
        _process.Add(processMs);
        if (_elapsed < Window) return null;
        string? line = null;
        if (_startupDropped)
        {
            string gpu = _gpu.Worst <= 0 ? "gpu n/a" : $"gpu {_gpu.Sum / _frames:0.00}/{_gpu.Worst:0.00}";
            line = $"fps-log (avg/worst ms): cpu {_cpu.Sum / _frames:0.00}/{_cpu.Worst:0.00}, {gpu}, "
                   + $"process {_process.Sum / _frames:0.00}/{_process.Worst:0.00}, {_frames / _elapsed:0} fps, "
                   + $"{drawCalls} draws, {primitives / 1000}k prims";
        }
        _startupDropped = true;
        _elapsed = 0;
        _frames = 0;
        _cpu.Reset();
        _gpu.Reset();
        _process.Reset();
        return line;
    }
}
