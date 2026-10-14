using Godot;

namespace Tank.Client.Art;

/// <summary>Godot side of <see cref="Palette"/>.</summary>
public static class PaletteColors
{
    /// <summary>sRGB palette entry as a Godot colour (Godot colours are sRGB; source_color uniforms linearise them).</summary>
    public static Color ToColor(this System.Numerics.Vector3 srgb, float alpha = 1f) => new(srgb.X, srgb.Y, srgb.Z, alpha);
}
