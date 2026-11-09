// Cartoon houses and their ruins (HouseShapes, drawn by HouseViews). Vertex colour RGB is each part's colour (linear):
// walls, roof, door, windows, chimney, rubble. The shared toon lighting with the walls' sky rim light (toon_rim_strength,
// which HouseViews sets to the rocks' 0.25); the base darkens a little near the ground so a house sits in the grass.
// A house near the camera or between it and the player's tank is screen-door dithered: opacity is a per-instance uniform
// HouseViews writes (1 solid). The shadow-only copy never has it lowered, so its shadow stays solid.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/toon_light.gdshaderinc"
#include "res://shaders/dither.gdshaderinc"

instance uniform float opacity = 1.0;
uniform float bottom_shade = 0.8;       // brightness at the ground

varying float height;

void vertex() {
	height = VERTEX.y;
}

void fragment() {
	if (opacity < 0.999 && bayer4(FRAGCOORD.xy) > opacity) discard;
	vec3 albedo = COLOR.rgb * mix(bottom_shade, 1.0, smoothstep(0.0, 1.2, height));
	ALBEDO = albedo;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
	EMISSION = toon_rim(NORMAL, VIEW) * mix(vec3(1.0), albedo * 2.0, 0.6);
}
