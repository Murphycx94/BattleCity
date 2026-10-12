using Godot;

namespace Tank.Client.View;

public enum CameraMode
{
    Overview,
    Chase,
}

/// <summary>
/// Overview: a high, narrow-FOV perspective that shows the whole board like the original, drifting slightly
/// toward the player for depth. Chase: third-person behind the player's tank. Neither affects gameplay.
/// </summary>
public partial class CameraRig : Node3D
{
    private const float OverviewPitchDeg = 56f;
    private const float OverviewFov = 32f;
    private const float ChaseFov = 62f;

    private readonly Camera3D _camera = new() { Current = true, Fov = OverviewFov, Far = 400 };
    private Vector3 _boardCenter;
    private Vector2 _boardSize = new(13, 13);
    private Vector3 _smoothedTarget;
    private Vector3 _smoothedPos;
    private bool _initialized;

    public CameraMode Mode { get; set; } = CameraMode.Overview;

    public override void _Ready() => AddChild(_camera);

    public void SetBoard(float width, float height)
    {
        _boardSize = new Vector2(width, height);
        _boardCenter = new Vector3(width / 2, 0, height / 2);
        _initialized = false;
    }

    public void Toggle() => Mode = Mode == CameraMode.Overview ? CameraMode.Chase : CameraMode.Overview;

    public void UpdateRig(float dt, Vector3? player, float playerYaw, float shake, double time)
    {
        Vector3 target, pos;
        if (Mode == CameraMode.Chase && player is { } p)
        {
            var forward = new Vector3(-Mathf.Sin(playerYaw), 0, -Mathf.Cos(playerYaw));
            target = p + forward * 2.2f + new Vector3(0, 0.3f, 0);
            pos = p - forward * 3.4f + new Vector3(0, 2.6f, 0);
            _camera.Fov = Mathf.Lerp(_camera.Fov, ChaseFov, dt * 4);
        }
        else
        {
            var drift = player is { } pp ? (pp - _boardCenter) * 0.08f : Vector3.Zero;
            target = _boardCenter + new Vector3(drift.X, 0, drift.Z) + new Vector3(0, 0, 0.9f);
            float distance = FitDistance();
            float pitch = Mathf.DegToRad(OverviewPitchDeg);
            pos = target + new Vector3(0, Mathf.Sin(pitch), Mathf.Cos(pitch)) * distance;
            _camera.Fov = Mathf.Lerp(_camera.Fov, OverviewFov, dt * 4);
        }

        if (!_initialized)
        {
            _smoothedTarget = target;
            _smoothedPos = pos;
            _initialized = true;
        }
        float k = 1f - Mathf.Exp(-dt * (Mode == CameraMode.Chase ? 7f : 3f));
        _smoothedTarget = _smoothedTarget.Lerp(target, k);
        _smoothedPos = _smoothedPos.Lerp(pos, k);

        var jitter = shake > 0
            ? new Vector3(Mathf.Sin((float)time * 71f), Mathf.Sin((float)time * 53f), 0) * shake * 0.12f
            : Vector3.Zero;
        _camera.GlobalPosition = _smoothedPos + jitter;
        _camera.LookAt(_smoothedTarget + jitter * 0.5f, Vector3.Up);
    }

    private float FitDistance()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        float aspect = viewport.Y > 0 ? viewport.X / viewport.Y : 16f / 9f;
        float vHalf = Mathf.DegToRad(OverviewFov) / 2;
        float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * aspect);
        float needV = (_boardSize.Y * 0.56f + 0.5f) / Mathf.Tan(vHalf);
        float needH = (_boardSize.X * 0.5f + 0.7f) / Mathf.Tan(hHalf);
        return Mathf.Max(needV, needH);
    }
}
