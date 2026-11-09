// Cartoon water (riverland design §6.3) on WaterMesh's flat sheets. UV.x is the signed depth (level − ground height),
// interpolated over the same triangles as the terrain, so it is 0 exactly on the drawn shore: negative (dry) fragments
// are discarded. The colour runs from shallow to deep with the depth, a white foam band sways along the shore, and a few
// flat lighter patches drift as ripples. Opaque (the project does not sort transparent geometry): the shallowest
// centimetres are screen-door dithered like the bushes, so the edge reads as see-through instead of a hard line.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/common.gdshaderinc"
#include "res://shaders/dither.gdshaderinc"
#include "res://shaders/toon_light.gdshaderinc"

uniform vec3 water_shallow : source_color = vec3(0.498, 0.839, 0.847);   // Palette.WaterShallow
uniform vec3 water_deep : source_color = vec3(0.243, 0.612, 0.769);      // Palette.WaterDeep
uniform vec3 foam_color : source_color = vec3(0.957, 0.984, 1.0);        // Palette.Foam
uniform float deep_at = 1.4;            // metres of water where the colour is all deep
uniform float foam_depth = 0.12;        // the foam covers water shallower than this...
uniform float foam_swell = 0.04;        // ...give or take this much, swaying with time
uniform float foam_speed = 1.3;
uniform float edge_depth = 0.06;        // water shallower than this dithers out towards the shore
uniform float ripple_scale = 0.12;      // ripple features per metre
uniform vec2 ripple_drift = vec2(0.06, 0.035);   // features per second the ripple pattern drifts
uniform float ripple_from = 0.66;       // noise above this is a ripple patch
uniform float ripple_amount = 0.22;     // how far a patch lightens towards the foam colour

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float depth = UV.x;
	if (depth < 0.0) discard;
	if (depth < edge_depth && bayer4(FRAGCOORD.xy) > depth / edge_depth) discard;
	vec3 colour = mix(water_shallow, water_deep, smoothstep(0.0, deep_at, depth));
	vec2 p = world_pos.xz * ripple_scale;
	float ripple = value_noise2(p + TIME * ripple_drift) * 0.65 + value_noise2(p * 2.7 - TIME * ripple_drift * 1.7 + vec2(5.2, 1.3)) * 0.35;
	colour = mix(colour, foam_color, step(ripple_from, ripple) * ripple_amount);
	float swell = sin(TIME * foam_speed + world_pos.x * 0.35 + world_pos.z * 0.27);
	if (depth < foam_depth + foam_swell * swell) colour = foam_color;
	ALBEDO = colour;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
