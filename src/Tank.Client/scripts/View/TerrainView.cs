using Godot;
using Tank.Core;
using Tank.Core.World;

namespace Tank.Client.View;

/// <summary>
/// Draws the cell grid with one MultiMesh per terrain type (a handful of draw calls for thousands of
/// bricks). Rebuilds only when <see cref="GameMap.Version"/> changes.
/// </summary>
public partial class TerrainView : Node3D
{
    private const float BrickHeight = 0.42f;
    private const float SteelHeight = 0.48f;
    private const float CanopyBottom = 0.62f;
    private const float CanopyHeight = 0.34f;

    private readonly Dictionary<CellType, MultiMeshInstance3D> _layers = new();
    private GameMap? _map;
    private int _version = -1;
    private Node3D? _ground;

    public override void _Ready()
    {
        float c = WorldMath.CellMeters;
        AddLayer(CellType.Brick, new BoxMesh { Size = new Vector3(c * 0.96f, BrickHeight, c * 0.96f) }, Palette.VertexColored(0.9f), BrickHeight / 2);
        AddLayer(CellType.Steel, new BoxMesh { Size = new Vector3(c * 0.98f, SteelHeight, c * 0.98f) }, Palette.Metal(Palette.Steel), SteelHeight / 2);
        AddLayer(CellType.Water, new BoxMesh { Size = new Vector3(c, 0.06f, c) }, WaterMaterial(), 0.03f);
        AddLayer(CellType.Forest, new BoxMesh { Size = new Vector3(c * 1.02f, CanopyHeight, c * 1.02f) }, ForestMaterial(), CanopyBottom + CanopyHeight / 2);
        AddLayer(CellType.Ice, new BoxMesh { Size = new Vector3(c, 0.02f, c) }, Palette.Metal(Palette.Ice), 0.01f);
    }

    public void Sync(GameMap map)
    {
        if (ReferenceEquals(map, _map) && map.Version == _version) return;
        if (!ReferenceEquals(map, _map)) BuildGround(map);
        _map = map;
        _version = map.Version;
        Rebuild(map);
    }

    private void Rebuild(GameMap map)
    {
        var counts = new Dictionary<CellType, int>();
        var cells = map.Cells;
        foreach (byte b in cells)
        {
            var t = (CellType)b;
            if (_layers.ContainsKey(t)) counts[t] = counts.GetValueOrDefault(t) + 1;
        }

        foreach (var (type, layer) in _layers)
        {
            var mm = layer.Multimesh;
            mm.InstanceCount = 0;
            mm.InstanceCount = counts.GetValueOrDefault(type);
            layer.Visible = mm.InstanceCount > 0;
        }

        var cursor = new Dictionary<CellType, int>();
        float half = WorldMath.CellMeters / 2;
        for (int cz = 0; cz < map.HeightCells; cz++)
        {
            for (int cx = 0; cx < map.WidthCells; cx++)
            {
                var type = (CellType)cells[cz * map.WidthCells + cx];
                if (!_layers.TryGetValue(type, out var layer)) continue;
                int i = cursor.GetValueOrDefault(type);
                cursor[type] = i + 1;
                float y = (float)layer.GetMeta("y");
                var pos = new Vector3(cx * WorldMath.CellMeters + half, y, cz * WorldMath.CellMeters + half);
                layer.Multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, pos));
                if (type == CellType.Brick)
                {
                    // Staggered two-tone courses read as masonry from the high camera.
                    bool alt = ((cx + (cz % 2)) / 2 + cz) % 2 == 0;
                    layer.Multimesh.SetInstanceColor(i, alt ? Palette.BrickA : Palette.BrickB);
                }
            }
        }
    }

    private void AddLayer(CellType type, Mesh mesh, Material material, float y)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = type == CellType.Brick,
            Mesh = mesh,
        };
        var instance = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material };
        instance.SetMeta("y", y);
        instance.CastShadow = type is CellType.Water or CellType.Ice
            ? GeometryInstance3D.ShadowCastingSetting.Off
            : GeometryInstance3D.ShadowCastingSetting.On;
        AddChild(instance);
        _layers[type] = instance;
    }

    private void BuildGround(GameMap map)
    {
        _ground?.QueueFree();
        _ground = new Node3D { Name = "Ground" };
        AddChild(_ground);
        float w = (float)map.WidthSub / Units.Tile, h = (float)map.HeightSub / Units.Tile;
        _ground.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(w, 0.1f, h) },
            MaterialOverride = GroundMaterial(w, h),
            Position = new Vector3(w / 2, -0.05f, h / 2),
        });

        // A raised steel frame around the playfield, like the grey border of the original.
        const float border = 0.5f, height = 0.3f;
        var frameMat = Palette.Metal(Palette.Frame);
        foreach (var (size, pos) in new[]
        {
            (new Vector3(w + 2 * border, height, border), new Vector3(w / 2, height / 2 - 0.1f, -border / 2)),
            (new Vector3(w + 2 * border, height, border), new Vector3(w / 2, height / 2 - 0.1f, h + border / 2)),
            (new Vector3(border, height, h), new Vector3(-border / 2, height / 2 - 0.1f, h / 2)),
            (new Vector3(border, height, h), new Vector3(w + border / 2, height / 2 - 0.1f, h / 2)),
        })
        {
            _ground.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = frameMat, Position = pos });
        }
    }

    private static Material GroundMaterial(float w, float h)
    {
        var shader = new Shader
        {
            Code = """
                shader_type spatial;
                uniform vec4 base : source_color;
                uniform vec4 line : source_color;
                uniform vec2 size;
                void fragment() {
                    vec2 p = UV * size;
                    vec2 g = abs(fract(p) - 0.5);
                    float tile = step(0.485, max(g.x, g.y));
                    float noise = fract(sin(dot(floor(p * 4.0), vec2(12.9898, 78.233))) * 43758.5453);
                    ALBEDO = mix(base.rgb * (0.94 + 0.06 * noise), line.rgb, tile * 0.8);
                    ROUGHNESS = 0.95;
                }
                """,
        };
        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("base", Palette.Ground);
        mat.SetShaderParameter("line", Palette.GroundLine);
        mat.SetShaderParameter("size", new Vector2(w, h));
        return mat;
    }

    private static Material WaterMaterial()
    {
        var shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode cull_disabled;
                uniform vec4 tint : source_color;
                void fragment() {
                    vec3 w = NODE_POSITION_WORLD + VERTEX;
                    float ripple = sin(w.x * 9.0 + TIME * 2.2) * sin(w.z * 7.0 - TIME * 1.7);
                    ALBEDO = tint.rgb + ripple * 0.06;
                    EMISSION = tint.rgb * 0.25;
                    ALPHA = tint.a;
                    METALLIC = 0.2;
                    ROUGHNESS = 0.15;
                }
                """,
        };
        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("tint", Palette.Water);
        return mat;
    }

    private static Material ForestMaterial()
    {
        var mat = Palette.Matte(Palette.Forest, 0.9f);
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        return mat;
    }
}
