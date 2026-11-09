// The blue tint round the screen while the own tank is outside the safe circle (stage 6 design §3.3), on a full-screen
// ColorRect under the rest of the match HUD. The distance from the centre is measured in the rectangle's own aspect
// (so the tint is round, not stretched) and normalised to 1 at a corner: nothing inside 0.35 of that, the full
// strength from 0.75 out, so the middle of the screen stays clear and the sides and corners go blue. ZoneWarningView
// fades `strength` 0 → 1 over a quarter second when the tank goes out and back when it comes in; `peak` is the opacity
// at the strongest.
shader_type canvas_item;

uniform vec3 tint : source_color = vec3(0.486, 0.796, 1.0);   // Palette.ZoneWall; the view sets it
uniform float strength : hint_range(0.0, 1.0) = 0.0;
uniform float peak : hint_range(0.0, 1.0) = 0.45;
uniform float aspect = 1.777778;   // the rectangle's width over its height; the view sets it on resize

void fragment() {
	vec2 half_extent = 0.5 * vec2(aspect, 1.0);
	float r = length((UV - 0.5) * vec2(aspect, 1.0)) / length(half_extent);
	COLOR = vec4(tint, peak * strength * smoothstep(0.35, 0.75, r));
}
