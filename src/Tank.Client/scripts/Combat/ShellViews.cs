using Godot;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;

namespace Tank.Client.Combat;

/// <summary>Glowing tracers for live shells, pooled. Drawn one tick behind the sim to match interpolated tanks.</summary>
public partial class ShellViews : Node3D
{
    private readonly List<MeshInstance3D> _pool = new();
    private StandardMaterial3D _material = null!;

    public override void _Ready() => _material = new StandardMaterial3D
    {
        AlbedoColor = new Color(1f, 0.85f, 0.45f),
        EmissionEnabled = true,
        Emission = new Color(1f, 0.7f, 0.3f),
        EmissionEnergyMultiplier = 4f,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    /// <param name="alpha">Fraction of the way from the previous tick to the latest one.</param>
    public void Sync(ReadOnlySpan<Shell> shells, float alpha)
    {
        while (_pool.Count < shells.Length)
        {
            var tracer = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.12f, 3f) }, MaterialOverride = _material };
            AddChild(tracer);
            _pool.Add(tracer);
        }
        for (int i = 0; i < _pool.Count; i++)
        {
            MeshInstance3D view = _pool[i];
            view.Visible = i < shells.Length;
            if (!view.Visible) continue;
            Shell shell = shells[i];
            Vector3 position = (shell.Position - shell.Velocity * ((1f - alpha) * SimConstants.Dt)).ToGodot();
            Vector3 heading = shell.Velocity.ToGodot().Normalized();
            if (Mathf.Abs(heading.Y) < 0.99f) view.LookAtFromPosition(position, position + heading, Vector3.Up);
            else view.Position = position;
        }
    }
}
