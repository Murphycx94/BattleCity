// Cartoon toy-tank paint: one plain body colour with the shared toon lighting, a slight darkening towards the bottom of
// the hull (about 85% at the ground) and a sky-coloured rim light. Modes:
//   0 paint  — hull, turret, mantlet, barrel; vertex colour R masks body (1) vs hub colour (0, the headlights). Meshes
//              without vertex colours read as white, so they are all body. height_offset lifts parts whose origin isn't
//              on the ground (the gun);
//   1 wheel  — cream hub, rubber tyre in the track colour (vertex colour R: 1 hub, 0 tyre);
//   2 track  — plain track colour (tracks, muzzle bulb).
shader_type spatial;
render_mode cull_back;

#include "res://shaders/toon_light.gdshaderinc"

uniform vec3 body_color : source_color = vec3(0.949, 0.541, 0.180);   // Palette.TankBody
uniform vec3 hub_color : source_color = vec3(0.961, 0.902, 0.784);    // Palette.TankHub
uniform vec3 track_color : source_color = vec3(0.227, 0.239, 0.290);  // Palette.Track
uniform int mode = 0;
uniform float bottom_shade = 0.85;       // brightness at the ground
uniform float full_height = 1.7;         // object-space height (plus height_offset) where the paint reaches full brightness
uniform float height_offset = 0.0;
uniform float wheel_shade = 0.9;         // the running gear sits low, so it gets roughly the bottom of the gradient

varying float object_height;

void vertex() {
	object_height = VERTEX.y;
}

void fragment() {
	vec3 albedo;
	if (mode == 2) {
		albedo = track_color;
	} else if (mode == 1) {
		albedo = mix(track_color, hub_color, COLOR.r) * wheel_shade;
	} else {
		albedo = mix(hub_color, body_color, COLOR.r);
		albedo *= mix(bottom_shade, 1.0, smoothstep(0.2, full_height, object_height + height_offset));
	}
	ALBEDO = albedo;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
	EMISSION = toon_rim(NORMAL, VIEW) * mix(vec3(1.0), albedo * 2.0, 0.8);   // the rim follows the paint, so dark tracks don't glow
}
