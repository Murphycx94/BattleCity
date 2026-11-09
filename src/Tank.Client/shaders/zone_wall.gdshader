// The zone's light wall (stage 6 design §3.3) on ZoneWallMesh's open unit cylinder: the vertex shader bends it onto the
// safe circle in world space (centre, radius), standing each column on the ground sampled under it (base_heights, a
// 256 × 1 strip ZoneWall fills with ZoneWallShape.SampleBase: texel k is column k, so the sample sits half a texel in
// from UV.x), sunk `sink` m into it and `height` m tall. Unlit, additive (the colour is how much light it adds, so no
// sorting is needed; the project does not sort transparent geometry), no depth write (the ground and the tanks occlude
// it, it occludes nothing), both sides drawn (the wall is seen from inside the circle and from out), unfogged. The light
// fades out from the foot to the top, slow vertical stripes drift round it, and it is brighter near the camera so it
// reads as a wall up close and as a thin bright line from afar.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, blend_add, fog_disabled, world_vertex_coords;

const float SEGMENTS = 256.0;     // ZoneWallShape.Segments: the base_heights strip's width
const float STRIPES = 360.0;      // vertical stripes round the whole circle
const float STRIPE_DRIFT = 0.6;   // rad/s the stripes move at
const float NEAR_FULL = 30.0;     // m from the camera: the wall at its brightest inside this …
const float NEAR_FAR = 250.0;     // … and at its dimmest beyond this
// The light added at the near and the far distance (the colour's share). Started at 1.0 / 0.35: up close the full colour
// over the sky or the grass saturated to white; from the overview the far band was faint. 0.6 / 0.45 read as a wall both ways.
const float NEAR_SHARE = 0.6;
const float FAR_SHARE = 0.45;

uniform vec2 centre = vec2(600.0, 600.0);   // the circle's centre on the ground (x, z); ZoneWall sets it every frame
uniform float radius = 400.0;               // and its radius, m
uniform float height = 60.0;                // ZoneWallShape.Height
uniform float sink = 3.0;                   // ZoneWallShape.Sink
uniform sampler2D base_heights : filter_linear, repeat_enable;
uniform vec3 wall_color : source_color = vec3(0.486, 0.796, 1.0);   // Palette.ZoneWall; ZoneWall sets it

void vertex() {
	float theta = UV.x * TAU;
	float base = textureLod(base_heights, vec2(UV.x + 0.5 / SEGMENTS, 0.5), 0.0).r;
	VERTEX = vec3(centre.x + radius * cos(theta), base - sink + UV.y * (height + sink), centre.y + radius * sin(theta));
}

void fragment() {
	float fade = 1.0 - smoothstep(0.0, 1.0, UV.y);
	// 0.75 ± 0.25 at first: added to the bright toon colours the troughs clipped to the same near-white as the crests
	// and the stripes vanished up close; the deeper troughs leave the ground showing between them.
	float stripe = 0.55 + 0.45 * sin(UV.x * TAU * STRIPES - TIME * STRIPE_DRIFT);
	float near = 1.0 - smoothstep(NEAR_FULL, NEAR_FAR, length(VERTEX));
	ALBEDO = wall_color * fade * stripe * mix(FAR_SHARE, NEAR_SHARE, near);
}
