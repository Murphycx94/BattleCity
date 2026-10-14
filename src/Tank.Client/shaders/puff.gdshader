// Cartoon smoke, dust and debris particles: opaque, toon-lit balls coloured by the particle colour. They never fade;
// their scale curve pops them up and shrinks them away instead, so there is no transparency to sort.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/toon_light.gdshaderinc"

void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
