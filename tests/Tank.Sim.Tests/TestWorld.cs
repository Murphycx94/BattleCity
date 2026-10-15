using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

internal static class TestWorld
{
    public const int Samples = 101;
    public const float Cell = 2f; // 200m × 200m
    public const float Size = (Samples - 1) * Cell;
    public const float Border = 4f;

    public static HeightField Flat(float height = 0f) =>
        new(Samples, Samples, Cell, Enumerable.Repeat(height, Samples * Samples).ToArray());

    /// <summary>Ground rising toward -Z (north) by <paramref name="grade"/> metres per metre.</summary>
    public static HeightField RampNorth(float grade)
    {
        var heights = new float[Samples * Samples];
        for (int j = 0; j < Samples; j++)
        for (int i = 0; i < Samples; i++)
            heights[j * Samples + i] = (Samples - 1 - j) * Cell * grade;
        return new HeightField(Samples, Samples, Cell, heights);
    }

    /// <summary>Flat south of <paramref name="edgeZ"/>, rising toward -Z at <paramref name="grade"/> beyond it.</summary>
    public static HeightField CliffNorth(float edgeZ, float grade)
    {
        var heights = new float[Samples * Samples];
        for (int j = 0; j < Samples; j++)
        for (int i = 0; i < Samples; i++)
            heights[j * Samples + i] = MathF.Max(0f, (edgeZ - j * Cell) * grade);
        return new HeightField(Samples, Samples, Cell, heights);
    }

    public static Battlefield Field(HeightField terrain, params Prop[] props) => new(terrain, props, Border);

    public static Battlefield FlatField(params Prop[] props) => Field(Flat(), props);

    public static TankState Tank(float x, float z, float yaw = 0f) =>
        new() { Id = 1, Spec = TankCatalog.Medium, Position = new Vector2(x, z), Yaw = yaw };

    public static InputCmd Cmd(sbyte throttle = 0, sbyte steer = 0, float aimYaw = 0f, float aimPitch = 0f, bool fire = false, AmmoType ammo = AmmoType.AP) =>
        new(0, throttle, steer, aimYaw, aimPitch, fire, ammo);

    public static void Drive(ref TankState t, Battlefield field, InputCmd cmd, int ticks, params Obb2[] obstacles)
    {
        for (int i = 0; i < ticks; i++) TankMotion.Drive(ref t, cmd, field, obstacles);
    }
}
