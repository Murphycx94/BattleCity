// Ground beyond the map edge: mostly the terrain's sandy dirt with large, soft grass blotches (about a third), like the
// playable ground at the edge, with the same mottling. Only ever seen far away and through fog.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/common.gdshaderinc"
#include "res://shaders/toon_mottle.gdshaderinc"
#include "res://shaders/toon_light.gdshaderinc"

uniform vec3 grass_light : source_color = vec3(0.608, 0.851, 0.29);   // Palette.GrassLight
uniform vec3 grass_dark : source_color = vec3(0.306, 0.604, 0.2);     // Palette.GrassDark
uniform float grass_tone = 0.55;
uniform vec3 dirt_color : source_color = vec3(0.831, 0.706, 0.549);   // Palette.Dirt
uniform float blotch_scale = 0.011;     // one blotch feature per ~90 m
uniform float grass_from = 0.55;        // blotch noise where grass starts showing...
uniform float grass_to = 0.65;          // ...and where it is all grass

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 p = world_pos.xz * blotch_scale;
	float n = value_noise2(p) * 0.7 + value_noise2(p * 2.3 + 17.0) * 0.3;
	float grass_w = smoothstep(grass_from, grass_to, n);
	vec3 grass = mix(grass_dark, grass_light, grass_tone);
	ALBEDO = mix(dirt_color, grass, grass_w) * toon_mottle(world_pos.xz);
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
