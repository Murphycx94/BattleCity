// Cartoon ground: grass, dirt and rock layers picked by slope, height and the baked dirt mask (mirrored in C# by
// GroundCover so grass grows only where the grass layer wins). The mask makes most of the map sandy dirt, with grass
// islands under the bush patches. Each layer is one flat palette colour times a large, soft light/dark mottling; layers
// blend smoothly. No textures.
// Layered mode (a map with ground kinds, riverland design §6.2): the mask is GroundLayers' RGBA (R not grass, G sand,
// B road, each blurred over 3×3 cells), R still drives the dirt weight exactly as the sandbox's mask does, G and B
// (by their share of R) recolour the flat dirt as earth, sand or road, and the ground darkens to a wet riverbed under
// the water level (not in the outer 8 m, where the drawn ground sinks below it). R is "not grass" dilated by a cell
// before the blur.
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
uniform bool layered = false;           // dirt_mask holds ground layers (RGBA), not the sandbox's dirt mask
uniform float water_level = -1000.0;    // world height of the water surface (layered maps)

uniform vec3 grass_light : source_color = vec3(0.608, 0.851, 0.29);   // Palette.GrassLight
uniform vec3 grass_dark : source_color = vec3(0.306, 0.604, 0.2);     // Palette.GrassDark
uniform float grass_tone = 0.55;        // the grass layer's colour between grass_dark and grass_light
uniform vec3 dirt_color : source_color = vec3(0.831, 0.706, 0.549);   // Palette.Dirt
uniform vec3 rock_color : source_color = vec3(0.620, 0.596, 0.565);   // Palette.Rock
uniform vec3 slope_earth : source_color = vec3(0.690, 0.565, 0.416);   // Palette.SlopeEarth; deeper earth on steep slopes
uniform vec3 earth_color : source_color = vec3(0.659, 0.545, 0.396);   // Palette.Earth; layered: dirt cells
uniform vec3 sand_color : source_color = vec3(0.902, 0.827, 0.627);    // Palette.Sand; layered: sand cells
uniform vec3 road_color : source_color = vec3(0.604, 0.580, 0.549);    // Palette.Road; layered: road cells
uniform vec3 riverbed_color : source_color = vec3(0.706, 0.604, 0.431);   // Palette.Riverbed; layered: under the water
uniform vec3 wet_sand_color : source_color = vec3(0.804, 0.710, 0.522);   // Palette.WetSand; layered: the water's edge

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
	vec3 flat_dirt = dirt_color;
	if (layered) {
		// Earth, sand where the G layer says, road over both where B says; the smoothsteps soften the blurred 2 m cells.
		// G and B count as their share of the not-grass layer R: R is dilated a cell further than they are, so that ring
		// takes the colour of the road or sand it surrounds instead of an earth verge; pure dirt (G = B = 0) stays earth.
		// A straight edge's ring centre has a share of only 1/2, so the windows sit low enough for it to be all road/sand.
		vec3 ground = texture(dirt_mask, world_pos.xz / map_size).rgb;
		float not_grass = max(ground.r, 0.001);
		float sand_share = min(ground.g / not_grass, 1.0);
		float road_share = min(ground.b / not_grass, 1.0);
		flat_dirt = mix(mix(earth_color, sand_color, smoothstep(0.15, 0.55, sand_share)), road_color, smoothstep(0.2, 0.5, road_share));
	}
	vec3 dirt_tint = mix(flat_dirt, slope_earth, slope_dirt / (slope_dirt + patchy + 0.0001));
	float grass_w = max(1.0 - dirt_w - rock_w, 0.0);

	vec3 grass = mix(grass_dark, grass_light, grass_tone);
	vec3 albedo = grass * grass_w + dirt_tint * dirt_w + rock_color * rock_w;
	if (layered) {
		// A wet band along the water's edge, deepening to the riverbed colour below it. Both fade out over the outer 8 m
		// (TerrainMesh.EdgeFadeMetres), where the drawn ground sinks to the far-ground level, under the water level even
		// on dry land: no wet rim around the map.
		float inland = smoothstep(0.0, 8.0, min(min(world_pos.x, map_size.x - world_pos.x), min(world_pos.z, map_size.y - world_pos.z)));
		albedo = mix(albedo, wet_sand_color, (1.0 - smoothstep(water_level, water_level + 0.25, world_pos.y)) * inland);
		albedo = mix(albedo, riverbed_color, (1.0 - smoothstep(water_level - 0.4, water_level, world_pos.y)) * inland);
	}
	ALBEDO = albedo * toon_mottle(world_pos.xz);
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
