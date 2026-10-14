using Godot;

namespace Tank.Client.Art;

/// <summary>Loads the cartoon shaders into materials and sets their parameters.</summary>
public static class Materials
{
    public static ShaderMaterial FromShader(string name) => new() { Shader = GD.Load<Shader>($"res://shaders/{name}.gdshader") };

    public static ShaderMaterial With(this ShaderMaterial material, string name, Variant value)
    {
        material.SetShaderParameter(name, value);
        return material;
    }
}
