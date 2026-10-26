// A base's ring on the ground (stage 5 design §3.4) on RingMesh's band: UV.x runs 0 on the inner edge to 1 on the
// outer. One flat colour, unlit, seen from both sides. The outermost 0.4 m (RingBand.SolidLine) is a solid line so the
// base's edge reads from afar; inwards of it the band fades from edge_opacity to nothing. Unlit on purpose: shadows
// (trees, tanks) do not darken the band, so where one crosses it the shadow looks cut off; a flat marking reads better. Opaque (the project does not sort transparent
// geometry): the fade is screen-door dithered like the bushes and the water's edge.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

#include "res://shaders/dither.gdshaderinc"

uniform vec3 ring_color : source_color = vec3(0.435, 0.561, 0.71);   // Palette.AllyBody; BaseRings sets each ring's
uniform float edge_opacity = 0.85;   // the faded band's opacity next to the solid line
uniform float solid_from = 0.778;    // UV.x where the solid line starts (BaseRings sets it: RingBand.SolidFrom)

void fragment() {
	float opacity = UV.x >= solid_from ? 1.0 : edge_opacity * smoothstep(0.0, solid_from, UV.x);
	if (bayer4(FRAGCOORD.xy) > opacity) discard;
	ALBEDO = ring_color;
}
