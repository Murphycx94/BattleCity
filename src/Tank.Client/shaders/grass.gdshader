// Cartoon grass clumps (MultiMesh). Vertex colour R = height along the blade (0 root → 1 tip), G = per-blade shade.
// INSTANCE_CUSTOM: r = unused, g = wind phase, b = brightness variation.
// Blades go from the palette's dark grass at the root to its light grass at the tip, each clump a little lighter or
// darker. They sway in the wind (more towards the tip), lie flat under the tank, and shrink away near the edge of the
// grass radius.
shader_type spatial;
render_mode cull_disabled, world_vertex_coords;

#include "res://shaders/toon_light.gdshaderinc"

uniform vec3 grass_dark : source_color = vec3(0.306, 0.604, 0.2);     // Palette.GrassDark
uniform vec3 grass_light : source_color = vec3(0.608, 0.851, 0.29);   // Palette.GrassLight
uniform float brightness_variation = 0.16;
uniform float blade_variation = 0.08;
uniform vec2 wind_direction = vec2(0.8, 0.6);
uniform float wind_strength = 0.08;     // metres of tip sway
uniform float wind_speed = 1.3;
uniform float fade_start = 66.0;        // metres from the streaming focus (the tank); GrassField.Radius is 90
uniform float fade_end = 84.0;
uniform float tank_margin = 0.7;        // blades lie flat inside the tank footprint and recover over this distance
uniform float flat_height = 0.1;        // fraction of its height a blade keeps under the tank

// Set every frame by GrassField (one tank for now; an array is the way to support several).
global uniform vec4 tank_footprint;     // centre x, centre z, half width, half length
global uniform vec4 tank_axes;          // right x, right z, forward x, forward z
global uniform vec2 grass_focus;

varying float height01;
varying float brightness;
varying float far_blend;

void vertex() {
	height01 = COLOR.r;
	brightness = 1.0 + brightness_variation * (INSTANCE_CUSTOM.b - 0.5) * 2.0 + blade_variation * (COLOR.g - 0.5) * 2.0;
	vec3 root = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float focus_distance = length(root.xz - grass_focus);
	float fade = 1.0 - smoothstep(fade_start, fade_end, focus_distance);
	far_blend = smoothstep(fade_start - 30.0, fade_end, focus_distance);

	float phase = dot(root.xz, wind_direction) * 0.18 + TIME * wind_speed + INSTANCE_CUSTOM.g * 6.2832;
	float gust = sin(phase) * 0.7 + sin(phase * 2.3 + 1.7) * 0.3;
	VERTEX.xz += normalize(wind_direction) * gust * wind_strength * height01 * height01;
	// Flatten blades inside the (grown) tank footprint, recovering smoothly outside it: signed distance to the box in the tank's frame.
	vec2 d = VERTEX.xz - tank_footprint.xy;
	vec2 local = vec2(dot(d, tank_axes.xy), dot(d, tank_axes.zw));
	vec2 q = abs(local) - tank_footprint.zw;
	float box_distance = length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0);
	float standing = mix(flat_height, 1.0, smoothstep(0.0, tank_margin, box_distance));
	VERTEX.y = root.y + (VERTEX.y - root.y) * standing;
	VERTEX = root + (VERTEX - root) * fade;
}

void fragment() {
	vec3 blade = mix(grass_dark, grass_light, smoothstep(0.0, 1.0, height01)) * brightness;
	// Towards the edge of the grass the blades move to the terrain's own grass tone, so the disc doesn't read darker than the ground around it.
	ALBEDO = mix(blade, mix(grass_dark, grass_light, 0.55), far_blend * 0.8);
	if (!FRONT_FACING) {
		// Back faces get the normal flipped by Godot and would shade as dark specks; lean them towards world-up instead.
		vec3 up_view = normalize((VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
		NORMAL = normalize(mix(NORMAL, up_view, 0.75));
	}
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
