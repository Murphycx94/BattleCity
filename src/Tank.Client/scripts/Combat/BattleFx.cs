using Godot;

namespace Tank.Client.Combat;

/// <summary>Short-lived glowing flashes: a muzzle blast when firing, a fireball where a shell lands.</summary>
public partial class BattleFx : Node3D
{
    public void Muzzle(Vector3 at) => Flash(at, new Color(1f, 0.8f, 0.45f), 1.6f, 0.12f);

    public void Explosion(Vector3 at) => Flash(at, new Color(1f, 0.55f, 0.2f), 4.5f, 0.45f);

    private void Flash(Vector3 at, Color color, float radius, float seconds)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var ball = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = material,
            Position = at,
            Scale = Vector3.One * (radius * 0.3f),
        };
        var light = new OmniLight3D { LightColor = color, LightEnergy = 4f, OmniRange = radius * 3f };
        ball.AddChild(light);
        AddChild(ball);

        Tween tween = ball.CreateTween();
        tween.TweenProperty(ball, "scale", Vector3.One * radius, seconds).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
        tween.Parallel().TweenProperty(material, "albedo_color", new Color(color, 0f), seconds);
        tween.Parallel().TweenProperty(light, "light_energy", 0f, seconds);
        tween.TweenCallback(Callable.From(ball.QueueFree));
    }
}
