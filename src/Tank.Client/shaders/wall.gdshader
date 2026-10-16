// Toy walls and fences: one flat colour per material with the shared toon lighting and a sky-coloured rim light
// (toon_rim_strength, which WallViews sets to the rocks' 0.25). On a standing wall, soft joint lines are drawn in object
// space (no textures): staggered bricks (mode 0) or big uneven stone blocks (mode 1). Mode 2 is plain colour times vertex
// colour R (rubble chunks; fence wood has none, so 1). The base darkens a little near the ground so walls sit in the grass.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/common.gdshaderinc"
#include "res://shaders/toon_light.gdshaderinc"

uniform vec3 base_color : source_color = vec3(0.769, 0.333, 0.227);   // Palette.Brick
uniform vec3 joint_color : source_color = vec3(0.910, 0.788, 0.659);  // Palette.BrickMortar
uniform int mode = 0;
uniform vec2 block = vec2(0.5, 0.25);   // one brick (or stone) along the wall × up, m
uniform float joint = 0.025;            // half-width of a joint line, m
uniform float bottom_shade = 0.8;       // brightness at the ground

varying vec3 local_pos;
varying vec3 local_normal;

void vertex() {
	local_pos = VERTEX;
	local_normal = NORMAL;
}

// 1 on a joint line, 0 inside a block. Every other row is shifted half a block; stones also get a random shift per row.
float joint_mask(vec2 p) {
	vec2 cell = p / block;
	float row = floor(cell.y);
	cell.x += mod(row, 2.0) * 0.5;
	if (mode == 1) cell.x += hash12(vec2(row, 3.7)) * 0.6;
	vec2 inside = fract(cell) * block;
	vec2 to_edge = min(inside, block - inside);
	float edge = min(to_edge.x, to_edge.y);
	float aa = fwidth(edge);
	return 1.0 - smoothstep(joint - aa, joint + aa, edge);
}

void fragment() {
	vec3 n = abs(normalize(local_normal));   // interpolated across a rounded edge it comes out short of unit length
	vec3 albedo = base_color;
	if (mode < 2) {
		vec2 p = n.y > 0.7 ? local_pos.xz : (n.x > n.z ? local_pos.zy : local_pos.xy);   // top, ends, faces
		albedo = mix(albedo, joint_color, joint_mask(p));
	} else {
		albedo *= COLOR.r;   // the chunk's own shade, 0.8 … 1 (1 where the mesh has no vertex colours)
	}
	albedo *= mix(bottom_shade, 1.0, smoothstep(0.0, 1.2, local_pos.y));
	ALBEDO = albedo;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
	EMISSION = toon_rim(NORMAL, VIEW) * mix(vec3(1.0), albedo * 2.0, 0.6);
}
