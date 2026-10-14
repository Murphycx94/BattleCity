// Tall cartoon bushes (MultiMesh, mesh from BushShape). Vertex colour: R = height along leaf/core (0..1),
// G = per-leaf shade, B = 1 on leaves, 0 on the core. INSTANCE_CUSTOM (written by BushField):
//   r = opacity (1 solid; lower = screen-door dithered so the player can see through bushes next to the camera,
//       around their own tank or between the camera and the tank), g = push strength (1 while the tank is inside, springing back to 0 after it leaves),
//   b, a = tank centre (x, z) to push away from; the tank's axes and half size come from the grass globals.
// Leaves sway more than grass; the whole bush lights like one soft ball (normals from BushShape, kept on back faces).
shader_type spatial;
render_mode cull_disabled, world_vertex_coords;

#include "res://shaders/toon_light.gdshaderinc"

uniform vec3 bush_dark : source_color = vec3(0.231, 0.49, 0.18);     // Palette.BushDark
uniform vec3 bush_light : source_color = vec3(0.486, 0.761, 0.263);  // Palette.BushLight
uniform bool dither = true;           // off on the shadow-only copy, so see-through bushes still cast solid shadows
uniform float shade_variation = 0.12;
uniform vec2 wind_direction = vec2(0.8, 0.6);
uniform float wind_strength = 0.22;     // metres of sway at the top of a leaf
uniform float wind_speed = 1.1;
uniform float core_sway = 0.3;          // the core moves this fraction of a leaf
uniform float push_distance = 0.6;      // metres a leaf is pushed out of the tank's way
uniform float push_reach = 1.2;         // leaves this far outside the hull are still pushed (fading out)

global uniform vec4 tank_footprint;     // centre x, centre z, half width, half length (BushField pushes from its own stored centre)
global uniform vec4 tank_axes;          // right x, right z, forward x, forward z

varying float height01;
varying float shade;
varying float opacity;

// 4×4 Bayer threshold in (0, 1) for the screen-door transparency.
float bayer4(vec2 frag) {
	ivec2 p = ivec2(mod(frag, 4.0));
	int i = p.x + p.y * 4;
	const float m[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
	return (m[i] + 0.5) / 16.0;
}

void vertex() {
	height01 = COLOR.r;
	shade = 1.0 + shade_variation * (COLOR.g - 0.5) * 2.0;
	opacity = INSTANCE_CUSTOM.r;
	vec3 root = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float sway_weight = height01 * height01 * mix(core_sway, 1.0, COLOR.b);

	float phase = dot(root.xz, wind_direction) * 0.12 + TIME * wind_speed + root.x * 0.37;
	float gust = sin(phase) * 0.7 + sin(phase * 1.9 + 1.3) * 0.3;
	VERTEX.xz += normalize(wind_direction) * gust * wind_strength * sway_weight;

	// Part around the tank: push horizontally away from the hull box centred at the stored tank position.
	float strength = INSTANCE_CUSTOM.g;
	if (strength > 0.001) {
		vec2 d = VERTEX.xz - INSTANCE_CUSTOM.ba;
		vec2 local = vec2(dot(d, tank_axes.xy), dot(d, tank_axes.zw));
		vec2 half_size = tank_footprint.zw;
		vec2 q = abs(local) - half_size;
		float box_distance = length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0);
		vec2 away_local = box_distance > 0.0 ? local - clamp(local, -half_size, half_size) : (q.x > q.y ? vec2(sign(local.x), 0.0) : vec2(0.0, sign(local.y)));
		vec2 away = normalize(away_local.x * tank_axes.xy + away_local.y * tank_axes.zw + vec2(1e-5));
		float amount = push_distance * strength * (1.0 - smoothstep(0.0, push_reach, box_distance)) * mix(0.3, 1.0, height01) * COLOR.b;
		VERTEX.xz += away * amount;
	}
}

void fragment() {
	if (dither && opacity < 0.999 && bayer4(FRAGCOORD.xy) > opacity) discard;
	if (!FRONT_FACING) NORMAL = -NORMAL;   // undo Godot's back-face flip: keep the bush's ball-shaped normals
	ALBEDO = mix(bush_dark, bush_light, smoothstep(0.0, 1.0, height01)) * shade;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
