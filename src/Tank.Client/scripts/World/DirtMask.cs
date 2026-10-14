using Godot;
using Tank.Client.Art.Meshes;

namespace Tank.Client.World;

/// <summary>
/// Where the map is bare dirt, 0 (grass) to 1 (dirt): mostly dirt, with grass islands under the bush patches and a few
/// more from seeded noise. Baked once and shared: the terrain shader samples it as a texture, the grass scatter reads the
/// same numbers, so grass blades grow only on the islands. The numbers live in the engine-neutral <see cref="DirtPatches"/>.
/// </summary>
public sealed class DirtMask
{
    public const int Resolution = DirtPatches.Resolution;
    private readonly DirtPatches _patches;

    public DirtMask(float sizeX, float sizeZ, IReadOnlyList<System.Numerics.Vector2> grassIslands, uint seed) =>
        _patches = new DirtPatches(sizeX, sizeZ, grassIslands, seed);

    public float SizeX => _patches.SizeX;
    public float SizeZ => _patches.SizeZ;

    public float At(float x, float z) => _patches.At(x, z);

    public ImageTexture ToTexture() =>
        ImageTexture.CreateFromImage(Image.CreateFromData(Resolution, Resolution, false, Image.Format.L8, _patches.ToBytes()));
}
