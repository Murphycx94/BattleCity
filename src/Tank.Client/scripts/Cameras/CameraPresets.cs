using Godot;
using SandboxMap = Tank.Sim.Maps.SandboxMap;
using SN = System.Numerics;

namespace Tank.Client.Cameras;

/// <summary>
/// Fixed viewpoints for repeatable screenshots and frame-time runs (<c>--camera-preset NAME</c>).
/// front/side follow the tank in its own frame; hill is a fixed overview; spin turns a full circle every 10 s from the
/// middle of the map, the worst case for draw distance; orbit circles the tank at 14 m every 12 s; bush starts the tank
/// inside the bush patch nearest the spawn and looks at it from outside the patch; target looks at a practice target (the
/// range demo's, else the first) from its front-right, on the side where a thrown turret lands (pair it with --range-demo);
/// yard looks down on the walled yard and the cover beside the spawn from the south-east; gate looks at the yard's fence
/// gate from just outside it (pair it with --range-demo fence); patrol starts the tank at <c>SandboxMap.PatrolWatch</c> and
/// looks at the patrolling target's first bush patch from beside it, to watch it drive in, vanish and give itself away
/// when it fires.
/// </summary>
public static class CameraPresets
{
    private const float SpinPeriod = 10f;
    private static readonly Vector3 TankFocus = new(0, 1.3f, 0);
    private static readonly Vector3 FrontOffset = new(-5.5f, 2.6f, -9.5f); // ahead-left of the tank (its forward is −Z)
    private static readonly Vector3 SideOffset = new(10.5f, 1.8f, 0.4f);  // right-hand side
    private static readonly Vector2 HillEye = new(150f, 392f);
    private static readonly Vector3 HillTarget = new(215f, 12f, 230f);
    private static readonly Vector2 SpinEye = new(200f, 200f);
    private const float OrbitPeriod = 12f;
    private const float OrbitRadius = 14f;
    private static readonly Vector2 YardEye = new(204f, 358f);          // south-east of the walled yard, behind the spawn
    private static readonly Vector3 YardTarget = new(166f, 0f, 334f);   // between the yard and the cover by the spawn
    private const float YardEyeHeight = 15f;
    private static readonly Vector2 GateEye = new(139f, 330.5f);       // just outside the yard's fence gate, to the north-west
    private static readonly Vector3 GateFocus = new(151f, 0.9f, 338f);  // the middle of the gate's north panel
    private const float GateEyeHeight = 3f;
    private static readonly Vector3 TargetEye = new(7f, 4.5f, -13f);    // front-right of the target (its forward is −Z)
    private static readonly Vector3 TargetFocus = new(1.5f, 1.6f, 0f);  // between the hull and where the turret lands
    private const float PatrolEyeOut = 32f;     // the patrol eye stands this far from its focus, towards the watch point
    private const float PatrolEyeHeight = 7f;
    private const float PatrolFocusAlong = 0.8f;   // the focus lies this share of the way from the patrol's start to its first bush patch

    /// <summary>Presets whose eye is far from the tank: world detail (grass) should then stream around the eye, not the tank.</summary>
    public static bool StreamsAroundCamera(string? name) => name is "hill" or "spin" or "patrol";

    /// <summary>Every preset's name.</summary>
    public static IReadOnlyList<string> Names { get; } =
        new[] { "front", "side", "close", "closerear", "sprocket", "hill", "spin", "orbit", "bush", "target", "yard", "gate", "patrol" };

    public static bool IsKnown(string? name) => name is not null && Names.Contains(name);

    /// <summary>
    /// Where the tank starts for this preset: "bush" parks it in the bush patch nearest the spawn, "patrol" at
    /// <c>SandboxMap.PatrolWatch</c>, the rest use the normal spawn.
    /// </summary>
    public static SN.Vector2 Spawn(string? name) => name switch
    {
        "bush" => SandboxMap.BushPatches[0],
        "patrol" => SandboxMap.PatrolWatch,
        _ => SandboxMap.Spawn,
    };

    /// <param name="groundAt">Terrain height at (x, z), used to keep fixed eyes above the ground.</param>
    /// <param name="targetIndex">The practice target the "target" preset looks at (index into <c>SandboxMap.TargetSpots</c>).</param>
    public static (Vector3 Eye, Vector3 Target) Get(string name, Vector3 tank, float tankYaw, double time, Func<float, float, float> groundAt, int targetIndex)
    {
        var yaw = new Basis(Vector3.Up, tankYaw);
        switch (name)
        {
            case "front":
                return (tank + yaw * FrontOffset, tank + TankFocus);
            case "side":
                return (tank + yaw * SideOffset, tank + TankFocus);
            case "close": // front-right three-quarter, tight on the sprocket, mantlet and the hull's nose
                return (tank + yaw * new Vector3(4.6f, 1.5f, -6.2f), tank + yaw * new Vector3(0, 1.2f, -1.2f));
            case "closerear": // rear-right three-quarter, idler, side skirt and the hull's tail
                return (tank + yaw * new Vector3(5.5f, 1.4f, 6.5f), tank + yaw * new Vector3(0, 1.1f, 1.2f));
            case "sprocket": // tight on the right-hand drive sprocket and the track around it
                return (tank + yaw * new Vector3(3.4f, 0.9f, -4.8f), tank + yaw * new Vector3(1.3f, 0.65f, -2.95f));
            case "bush": // the tank parked inside a bush patch, seen from outside it (west, towards the lane)
                return (tank + new Vector3(-16f, 3.4f, 6f), tank + TankFocus);
            case "target":
                var spot = SandboxMap.TargetSpots[targetIndex];
                var at = new Vector3(spot.Position.X, groundAt(spot.Position.X, spot.Position.Y), spot.Position.Y);
                var turn = new Basis(Vector3.Up, spot.Yaw);
                return (at + turn * TargetEye, at + turn * TargetFocus);
            case "yard":
                return (new Vector3(YardEye.X, groundAt(YardEye.X, YardEye.Y) + YardEyeHeight, YardEye.Y),
                    YardTarget + Vector3.Up * groundAt(YardTarget.X, YardTarget.Z));
            case "gate":
                return (new Vector3(GateEye.X, groundAt(GateEye.X, GateEye.Y) + GateEyeHeight, GateEye.Y), GateFocus + Vector3.Up * groundAt(GateFocus.X, GateFocus.Z));
            case "patrol": // the patrol's drive from its start into its first bush patch, from the watch point's side
                SN.Vector2 focus = SN.Vector2.Lerp(SandboxMap.PatrolStart, SandboxMap.PatrolRoute[0].Position, PatrolFocusAlong);
                SN.Vector2 stand = focus + SN.Vector2.Normalize(SandboxMap.PatrolWatch - focus) * PatrolEyeOut;
                return (new Vector3(stand.X, groundAt(stand.X, stand.Y) + PatrolEyeHeight, stand.Y),
                    new Vector3(focus.X, groundAt(focus.X, focus.Y) + 1.5f, focus.Y));
            case "hill":
                return (new Vector3(HillEye.X, groundAt(HillEye.X, HillEye.Y) + 28f, HillEye.Y), HillTarget);
            case "orbit": // circles the parked tank at 14 m, level with the turret: stresses anything keyed to the camera
                float around = (float)(time / OrbitPeriod * Mathf.Tau);
                return (tank + new Vector3(Mathf.Sin(around) * OrbitRadius, 3.2f, Mathf.Cos(around) * OrbitRadius), tank + TankFocus);
            default:
                float angle = (float)(time / SpinPeriod * Mathf.Tau);
                var eye = new Vector3(SpinEye.X, groundAt(SpinEye.X, SpinEye.Y) + 8f, SpinEye.Y);
                return (eye, eye + new Vector3(Mathf.Sin(angle), -0.12f, Mathf.Cos(angle)) * 10f);
        }
    }
}
