// Cartoon boulders and pebbles: one flat palette colour that lightens towards the top faces, a faint large mottling
// so big rocks aren't perfectly flat, and a sky-coloured rim light. No textures.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/common.gdshaderinc"
#include "res://shaders/toon_light.gdshaderinc"

uniform vec3 rock_color : source_color = vec3(0.620, 0.596, 0.565);   // Palette.Rock
uniform vec3 rock_top : source_color = vec3(0.741, 0.714, 0.659);     // Palette.RockTop
uniform float brightness = 1.0;
uniform float bottom_shade = 0.72;      // brightness at the base, so rocks sit in the grass instead of floating on it
uniform float mottle_amount = 0.05;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	float top = smoothstep(0.35, 0.85, normalize(world_normal).y);
	float mottle = value_noise3(world_pos * 0.6) - 0.5;
	float base = mix(bottom_shade, 1.0, smoothstep(0.1, 0.55, COLOR.r));   // vertex colour R: height up the rock
	ALBEDO = mix(rock_color, rock_top, top) * brightness * base * (1.0 + mottle * 2.0 * mottle_amount);
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
	EMISSION = toon_rim(NORMAL, VIEW);
}

