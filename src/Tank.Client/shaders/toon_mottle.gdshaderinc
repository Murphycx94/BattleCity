// Large soft mottling for the terrain and far ground only. Include after common.gdshaderinc (needs value_noise2).

uniform float mottle_large = 40.0;      // metres per feature
uniform float mottle_small = 12.0;
uniform float mottle_amount = 0.12;     // ± brightness

// Large soft light/dark mottling shared by the terrain and the far ground (needs value_noise2 from common.gdshaderinc).
float toon_mottle(vec2 p) {
	float m = (value_noise2(p / mottle_large) - 0.5) + (value_noise2(p / mottle_small + vec2(7.3, 2.1)) - 0.5) * 0.6;
	return 1.0 + m * 2.0 * mottle_amount / 1.6;
}
