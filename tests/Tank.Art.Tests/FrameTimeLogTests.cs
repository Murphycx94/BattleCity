using Tank.Client.Sandbox;

namespace Tank.Art.Tests;

public class FrameTimeLogTests
{
    private const double Frame = 0.25;   // four frames fill a window

    private static string? AddSecond(FrameTimeLog log, long allocAtStart, long allocPerFrame)
    {
        string? line = null;
        for (int i = 0; i < 4; i++)
            line = log.Add(Frame, cpuMs: 2, gpuMs: 3, processMs: 4, drawCalls: 100, primitives: 50_000, allocAtStart + (i + 1) * allocPerFrame);
        return line;
    }

    [Fact]
    public void The_startup_window_is_dropped_and_the_second_reports()
    {
        var log = new FrameTimeLog();

        Assert.Null(AddSecond(log, 0, 1024));
        string? line = AddSecond(log, 4 * 1024, 1024);

        Assert.NotNull(line);
        Assert.Contains("4 fps", line);
        Assert.Contains("100 draws", line);
        Assert.Contains("50k prims", line);
    }

    [Fact]
    public void The_allocation_rate_is_the_windows_delta_per_second()
    {
        var log = new FrameTimeLog();
        AddSecond(log, 0, 0);

        // 2048 B over 4 frames of 0.25 s = 8192 B/s = 8.0 KB/s; the window starts at the first frame's figure.
        string? line = AddSecond(log, 0, 2048);

        Assert.NotNull(line);
        Assert.Contains("alloc 6.0 KB/s", line);   // deltas are measured from the window's first sample: 3 × 2048 B / 1 s
    }

    [Fact]
    public void Without_the_allocation_figure_the_field_stays_out()
    {
        var log = new FrameTimeLog();
        for (int i = 0; i < 4; i++) log.Add(Frame, 2, 3, 4, 100, 50_000);
        string? line = null;
        for (int i = 0; i < 4; i++) line = log.Add(Frame, 2, 3, 4, 100, 50_000);

        Assert.NotNull(line);
        Assert.DoesNotContain("alloc", line);
    }
}
