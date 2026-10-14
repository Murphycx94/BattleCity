// Cartoon background trees (MultiMesh). Vertex colour RGB = base colour (linear, already darker towards the ground),
// A = how much that part sways. INSTANCE_CUSTOM: r = brightness variation, g = wind phase, b = hue shift
// (a little bluer .. a little yellower), applied to foliage only.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/toon_light.gdshaderinc"

uniform float wind_strength = 0.03;     // fraction of height the crown leans in a gust
uniform float wind_speed = 0.7;
uniform float variation = 0.2;
uniform float hue_shift = 0.18;

varying vec3 base_color;

void vertex() {
	float h = INSTANCE_CUSTOM.b - 0.5;
	float foliage = step(0.01, COLOR.a);
	vec3 hue = vec3(1.0 + hue_shift * h, 1.0, 1.0 - hue_shift * h);
	base_color = COLOR.rgb * (1.0 - variation * 0.5 + variation * INSTANCE_CUSTOM.r) * mix(vec3(1.0), hue, foliage);
	float phase = TIME * wind_speed + INSTANCE_CUSTOM.g * 6.2832;
	float gust = sin(phase) * 0.7 + sin(phase * 2.1 + 0.6) * 0.3;
	VERTEX.x += gust * wind_strength * COLOR.a * VERTEX.y;
}

void fragment() {
	ALBEDO = base_color;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
