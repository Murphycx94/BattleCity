// Screen-door transparency for opaque shaders (the project does not sort transparent geometry): discard the fragments
// whose threshold is above the wanted opacity, e.g. `if (bayer4(FRAGCOORD.xy) > opacity) discard;`.
// Used by the bushes (see-through near the camera) and the water (its shallowest edge).

// 4×4 Bayer threshold in (0, 1) for the screen-door transparency.
float bayer4(vec2 frag) {
	ivec2 p = ivec2(mod(frag, 4.0));
	int i = p.x + p.y * 4;
	const float m[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
	return (m[i] + 0.5) / 16.0;
}
