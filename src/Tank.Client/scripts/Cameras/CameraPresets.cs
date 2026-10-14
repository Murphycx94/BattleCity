using Godot;

namespace Tank.Client.Cameras;

/// <summary>
/// Fixed viewpoints for repeatable screenshots and frame-time runs (<c>--camera-preset NAME</c>).
/// front/side follow the tank in its own frame; hill is a fixed overview; spin turns a full circle every 10 s from the
/// middle of the map, the worst case for draw distance; orbit circles the tank at 14 m every 12 s; bush starts the tank
/// inside the bush patch nearest the spawn and looks at it from outside the patch.
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

    /// <summary>Presets whose eye is far from the tank: world detail (grass) should then stream around the eye, not the tank.</summary>
    public static bool StreamsAroundCamera(string? name) => name is "hill" or "spin";

    public static bool IsKnown(string? name) => name is "front" or "side" or "close" or "closerear" or "sprocket" or "hill" or "spin" or "orbit" or "bush";

    /// <summary>Where the tank starts for this preset: "bush" parks it in the bush patch nearest the spawn, the rest use the normal spawn.</summary>
    public static System.Numerics.Vector2 Spawn(string? name) => name == "bush" ? Tank.Sim.Maps.SandboxMap.BushPatches[0] : Tank.Sim.Maps.SandboxMap.Spawn;

    /// <param name="groundAt">Terrain height at (x, z), used to keep fixed eyes above the ground.</param>
    public static (Vector3 Eye, Vector3 Target) Get(string name, Vector3 tank, float tankYaw, double time, Func<float, float, float> groundAt)
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
