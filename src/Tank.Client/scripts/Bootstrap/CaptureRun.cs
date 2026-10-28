using Godot;
using Tank.Client.Sandbox;

namespace Tank.Client.Bootstrap;

/// <summary>
/// The Godot side of a scripted run's captures, shared by the sandbox and the match: takes the screenshot the frame its
/// <see cref="ShotPlan"/> says it falls due, logs render costs once a second with <c>--fps-log</c>
/// (<see cref="FrameTimeLog"/>'s line) and quits the tree at <c>--quit-after</c>. Call <see cref="EndFrame"/> at the end
/// of every frame with the owner's clock — wall time in the sandbox and a live match, the virtual clock of a loopback
/// run — so the schedule matches what the mode simulates — and whether the scene's map has its picture yet: a due
/// screenshot waits for it (<see cref="ShotPlan.MapWaitSeconds"/> at most, then goes ahead with an error in the log), and
/// <c>--quit-after</c> waits with it. Build it only once the host is in the tree: the fps log turns on render-time
/// measuring for the host's viewport.
/// </summary>
public sealed class CaptureRun
{
    private readonly LaunchOptions _options;
    private readonly Node _host;
    private readonly ShotPlan _plan;
    private readonly FrameTimeLog _frameLog = new();

    /// <param name="host">The node whose viewport is captured and measured, and whose tree is quit.</param>
    public CaptureRun(LaunchOptions options, Node host, ShotPlan plan)
    {
        (_options, _host, _plan) = (options, host, plan);
        if (options.FpsLog) RenderingServer.ViewportSetMeasureRenderTime(host.GetViewport().GetViewportRid(), true);
    }

    /// <summary>Whether a due screenshot is being held for the map (an owner that quits on its own clock waits for it too).</summary>
    public bool HoldingScreenshot => _plan.HoldingForMap;

    /// <summary>
    /// Call at the end of every frame: logs render costs, takes the screenshot when due (held while <paramref name="mapReady"/>
    /// is false, the scene's map still without its picture) and quits when time is up.
    /// </summary>
    public void EndFrame(double delta, double time, bool mapReady)
    {
        if (_options.FpsLog) LogFrame(delta);
        if (_options.ScreenshotPath is { } path)
        {
            ShotCall call = _plan.TakeScreenshot(time, mapReady);
            if (call == ShotCall.TakeWithoutMap) GD.PrintErr("[minimap] screenshot taken before the map was baked");
            if (call != ShotCall.None)
            {
                Error error = _host.GetViewport().GetTexture().GetImage().SavePng(path);
                GD.Print(error == Error.Ok ? $"Screenshot saved: {path}" : $"Screenshot failed ({error}): {path}");
            }
        }
        if (_plan.QuitDue(time)) _host.GetTree().Quit();
    }

    private void LogFrame(double delta)
    {
        Rid rid = _host.GetViewport().GetViewportRid();
        string? line = _frameLog.Add(delta,
            RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid), RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid),
            Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0,
            (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame),
            (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame),
            GC.GetTotalAllocatedBytes());   // the whole process's managed churn: the zero-allocation audit reads this
        if (line is not null) GD.Print(line);
    }
}
