namespace Tank.Client.Sandbox;

/// <summary>
/// Collects per-frame render costs and reports average and worst once per second (<c>--fps-log</c>). Wall-clock frame time
/// is meaningless under the 60 fps cap, so CPU render, GPU render and process time (all ms) are tracked separately,
/// together with the latest frame's draw calls and primitives, and — when the caller passes the runtime's cumulative
/// managed-allocation figure — the window's allocation rate, the zero-allocation audit's number (blueprint §5 T10): a
/// steady per-frame allocation shows as a KB/s floor that never settles. The first (startup) window is dropped.
/// The report line itself is this code's only allocation, once a second, and is part of the figure it prints.
/// </summary>
public sealed class FrameTimeLog
{
    private const double Window = 1.0;
    private double _elapsed;
    private int _frames;
    private bool _startupDropped;
    private long _allocAtWindowStart = -1;
    private readonly Stat _cpu = new(), _gpu = new(), _process = new();

    private sealed class Stat
    {
        public double Sum, Worst;
        public void Add(double v) { Sum += v; Worst = Math.Max(Worst, v); }
        public void Reset() => Sum = Worst = 0;
    }

    /// <param name="allocatedBytes">The runtime's cumulative managed allocation (<c>GC.GetTotalAllocatedBytes</c>), or -1 to leave the figure out.</param>
    /// <returns>A report line when a full second has been collected, otherwise null.</returns>
    public string? Add(double frameSeconds, double cpuMs, double gpuMs, double processMs, long drawCalls, long primitives, long allocatedBytes = -1)
    {
        if (_frames == 0) _allocAtWindowStart = allocatedBytes;
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
            string alloc = allocatedBytes < 0 || _allocAtWindowStart < 0 ? "" : $", alloc {(allocatedBytes - _allocAtWindowStart) / _elapsed / 1024:0.0} KB/s";
            line = $"fps-log (avg/worst ms): cpu {_cpu.Sum / _frames:0.00}/{_cpu.Worst:0.00}, {gpu}, "
                   + $"process {_process.Sum / _frames:0.00}/{_process.Worst:0.00}, {_frames / _elapsed:0} fps, "
                   + $"{drawCalls} draws, {primitives / 1000}k prims{alloc}";
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
