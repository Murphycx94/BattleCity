using Godot;
using Tank.Sim.Terrain;

namespace Tank.Client.Cameras;

/// <summary>
/// Third-person camera orbiting the tank, driven by the mouse; sniper mode puts the eye above the gun with 2×/4×/8×
/// zoom. The screen centre is always where the player wants to aim. Pitch is positive looking down.
/// </summary>
public partial class OrbitCamera : Node3D
{
    private const float MinDistance = 8f;
    private const float MaxDistance = 30f;
    private const float DistanceStep = 2f;
    private const float FocusHeight = 1.6f;
    private const float GroundClearance = 1.2f;
    private const float MinPitch = -0.6f;
    private const float MaxPitch = 1.1f;
    private const float MouseSensitivity = 0.0025f;
    private const float BaseFov = 70f;
    private static readonly float[] Zooms = { 2f, 4f, 8f };

    private readonly Camera3D _camera = new() { Current = true, Fov = BaseFov, Near = 0.1f, Far = 2000f };
    private int _zoomIndex;

    public Camera3D Camera => _camera;
    public float Yaw { get; private set; }
    public float Pitch { get; private set; } = 0.25f;
    public float Distance { get; private set; } = 14f;
    public bool Sniper { get; private set; }
    public float Zoom => Zooms[_zoomIndex];

    public override void _Ready() => AddChild(_camera);

    public void Look(Vector2 mouseDelta)
    {
        float scale = MouseSensitivity / (Sniper ? Zoom : 1f);
        Yaw = Mathf.Wrap(Yaw - mouseDelta.X * scale, -Mathf.Pi, Mathf.Pi);
        Pitch = Mathf.Clamp(Pitch + mouseDelta.Y * scale, MinPitch, MaxPitch);
    }

    public void SetAngles(float yaw, float pitch)
    {
        Yaw = yaw;
        Pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
    }

    /// <summary>Positive = wheel up: zoom in (closer orbit or higher sniper magnification).</summary>
    public void Scroll(int steps)
    {
        if (Sniper) _zoomIndex = Math.Clamp(_zoomIndex + steps, 0, Zooms.Length - 1);
        else Distance = Mathf.Clamp(Distance - steps * DistanceStep, MinDistance, MaxDistance);
    }

    public void ToggleSniper() => Sniper = !Sniper;

    /// <param name="pivot">Hull origin the orbit circles.</param>
    /// <param name="sniperEye">Eye position in sniper mode, just above the gun.</param>
    public void Follow(Vector3 pivot, Vector3 sniperEye, HeightField terrain)
    {
        _camera.Rotation = new Vector3(-Pitch, Yaw, 0);
        if (Sniper)
        {
            _camera.Position = sniperEye;
            _camera.Fov = BaseFov / Zoom;
            return;
        }
        _camera.Fov = BaseFov;
        Vector3 look = -_camera.Basis.Z;
        Vector3 eye = pivot + Vector3.Up * FocusHeight - look * Distance;
        float floor = terrain.HeightAt(eye.X, eye.Z) + GroundClearance;
        if (eye.Y < floor) eye.Y = floor;
        _camera.Position = eye;
    }
}
