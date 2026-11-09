using Godot;
using Tank.Client.Art.Meshes;
using Tank.Sim.Maps;

namespace Tank.Client.World;

/// <summary>
/// Where the map is bare ground, 0 (grass) to 1 (dirt, sand or road). Baked once and shared: the terrain shader samples it
/// as a texture, the grass scatter reads the same numbers, so grass blades grow only where the drawn grass layer is.
/// Two kinds: a map without ground kinds (the sandbox) is mostly dirt with grass islands under the bush patches and a few
/// more from seeded noise (<see cref="DirtPatches"/>, an L8 texture); a map with ground kinds or water is layered
/// (<see cref="GroundLayers"/>, an RGBA texture whose red channel is "not grass" and whose green and blue tell the
/// shader where sand and road are). The numbers live in those engine-neutral classes.
/// </summary>
public sealed class DirtMask
{
    public const int Resolution = DirtPatches.Resolution;
    private readonly DirtPatches? _patches;
    private readonly GroundLayers? _layers;

    public DirtMask(float sizeX, float sizeZ, IReadOnlyList<System.Numerics.Vector2> grassIslands, uint seed) =>
        _patches = new DirtPatches(sizeX, sizeZ, grassIslands, seed);

    private DirtMask(GroundLayers layers) => _layers = layers;

    /// <summary>The layered mask of a field with water or ground other than grass; null for an all-grass, dry field.</summary>
    public static DirtMask? FromField(Battlefield field) =>
        GroundLayers.Needed(field.Surface, field.WaterLevel) ? new DirtMask(new GroundLayers(field.Surface)) : null;

    /// <summary>The texture holds ground layers (RGBA) for the terrain shader's layered mode.</summary>
    public bool IsLayered => _layers is not null;

    public float SizeX => _layers?.SizeX ?? _patches!.SizeX;
    public float SizeZ => _layers?.SizeZ ?? _patches!.SizeZ;

    public float At(float x, float z) => _layers?.NotGrassAt(x, z) ?? _patches!.At(x, z);

    public ImageTexture ToTexture() => _layers is { } layers
        ? ImageTexture.CreateFromImage(Image.CreateFromData(layers.Width, layers.Height, false, Image.Format.Rgba8, layers.ToRgba()))
        : ImageTexture.CreateFromImage(Image.CreateFromData(Resolution, Resolution, false, Image.Format.L8, _patches!.ToBytes()));
}
