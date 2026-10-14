// Cartoon ground: grass, dirt and rock layers picked by slope, height and the baked dirt mask (mirrored in C# by
// GroundCover so grass grows only where the grass layer wins). The mask makes most of the map sandy dirt, with grass
// islands under the bush patches. Each layer is one flat palette colour times a large, soft light/dark mottling; layers
// blend smoothly. No textures.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/common.gdshaderinc"
#include "res://shaders/toon_mottle.gdshaderinc"
#include "res://shaders/toon_light.gdshaderinc"

uniform sampler2D dirt_mask : hint_default_black, filter_linear, repeat_disable;
uniform vec2 map_size = vec2(400.0, 400.0);
uniform float grass_max_slope = 27.0;   // degrees; set from GroundCover
uniform float rock_min_slope = 38.0;
uniform float dry_height = 22.0;        // hill tops above this turn patchy (not mirrored by GroundCover)

uniform vec3 grass_light : source_color = vec3(0.608, 0.851, 0.29);   // Palette.GrassLight
uniform vec3 grass_dark : source_color = vec3(0.306, 0.604, 0.2);     // Palette.GrassDark
uniform float grass_tone = 0.55;        // the grass layer's colour between grass_dark and grass_light
uniform vec3 dirt_color : source_color = vec3(0.831, 0.706, 0.549);   // Palette.Dirt
uniform vec3 rock_color : source_color = vec3(0.620, 0.596, 0.565);   // Palette.Rock
uniform vec3 slope_earth : source_color = vec3(0.690, 0.565, 0.416);   // Palette.SlopeEarth; deeper earth on steep slopes

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 n = normalize(world_normal);
	// Layer weights: keep in step with GroundCover.GrassWeight.
	float slope = degrees(acos(clamp(n.y, 0.0, 1.0))) + (value_noise2(world_pos.xz * 0.35) - 0.5) * 6.0;
	float dirt_patch = texture(dirt_mask, world_pos.xz / map_size).r;
	dirt_patch = smoothstep(0.25, 0.95, dirt_patch + (value_noise2(world_pos.xz * 2.2) - 0.5) * 0.45);
	float dry = smoothstep(dry_height, dry_height + 10.0, world_pos.y) * 0.6 * (0.35 + 0.65 * value_noise2(world_pos.xz * 0.15));
	float rock_w = smoothstep(rock_min_slope - 4.0, rock_min_slope + 3.0, slope);
	float slope_dirt = smoothstep(grass_max_slope - 2.0, grass_max_slope + 6.0, slope);
	float dirt_w = max(max(slope_dirt, dirt_patch), dry) * (1.0 - rock_w);
	// Colour only: mask dirt (the flat ground) is light sand, slope-driven dirt a deeper earth (weights above are unchanged).
	float patchy = max(dirt_patch, dry);
	vec3 dirt_tint = mix(dirt_color, slope_earth, slope_dirt / (slope_dirt + patchy + 0.0001));
	float grass_w = max(1.0 - dirt_w - rock_w, 0.0);

	vec3 grass = mix(grass_dark, grass_light, grass_tone);
	vec3 albedo = grass * grass_w + dirt_tint * dirt_w + rock_color * rock_w;
	ALBEDO = albedo * toon_mottle(world_pos.xz);
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
