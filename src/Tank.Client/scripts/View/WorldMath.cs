using Godot;
using Tank.Core;
using Tank.Core.Math;

namespace Tank.Client.View;

/// <summary>Simulation sub units to Godot metres: one tile (one tank) = 1 m. Gameplay plane is XZ.</summary>
public static class WorldMath
{
    public const float CellMeters = (float)Units.Cell / Units.Tile;

    public static Vector3 ToWorld(double xSub, double zSub, float y = 0f) =>
        new((float)(xSub / Units.Tile), y, (float)(zSub / Units.Tile));

    /// <summary>Yaw for a model whose forward is -Z.</summary>
    public static float Yaw(Dir d) => d switch
    {
        Dir.Up => 0f,
        Dir.Right => -Mathf.Pi / 2,
        Dir.Down => Mathf.Pi,
        _ => Mathf.Pi / 2,
    };
}
