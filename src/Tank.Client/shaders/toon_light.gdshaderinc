// Shared cartoon lighting (design §4). Include it in a spatial shader to replace Godot's PBR light():
// - wrapped diffuse, saturate((N·L + w) / (1 + w)), so faces turned away from the sun never go black;
// - where the sun doesn't reach (turned away or in shadow), the colour moves towards the palette's blue-violet
//   Shadow colour instead of just getting darker;
// - no specular highlights. Other lights (the flash and fireball lamps) add plain wrapped diffuse.
// The sky-coloured ambient light from the environment is added by Godot on top.
// Assumes exactly one DirectionalLight3D (the sun); a second directional light would add the shaded fill again.
// Rim light is opt-in per shader: add toon_rim(NORMAL, VIEW) to EMISSION in fragment().

uniform vec3 toon_shadow_color : source_color = vec3(0.357, 0.353, 0.549);  // Palette.Shadow
uniform float toon_wrap = 0.5;
uniform float toon_shadow_mix = 0.5;      // how far the unlit colour moves towards toon_shadow_color
uniform float toon_shadow_level = 0.55;   // brightness of the sun's fill on the unlit side (0 = only ambient)
uniform vec3 toon_rim_color : source_color = vec3(0.867, 0.949, 0.984);     // Palette.SkyHorizon
uniform float toon_rim_strength = 0.0;    // 0.25 on tanks, rocks and walls

vec3 toon_rim(vec3 normal, vec3 view) {
	return toon_rim_color * toon_rim_strength * pow(1.0 - clamp(dot(normal, view), 0.0, 1.0), 3.0);
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	float wrapped = clamp((ndl + toon_wrap) / (1.0 + toon_wrap), 0.0, 1.0);
	vec3 sun = LIGHT_COLOR / PI;   // Godot pre-multiplies LIGHT_COLOR by PI
	if (LIGHT_IS_DIRECTIONAL) {
		// ATTENUATION carries the shadow map; fade it out towards the terminator so it can't cancel the wrapped diffuse there.
		float shadow = mix(1.0, ATTENUATION, smoothstep(0.0, 0.2, ndl));
		float lit = wrapped * shadow;
		// DIFFUSE_LIGHT is multiplied by ALBEDO afterwards, so express "albedo mixed towards the shadow colour" as a
		// per-channel factor on the albedo.
		// Clamped to the albedo itself, so the shade never lightens a channel: dark paint (tracks, tyres) keeps its
		// value and takes only the blue-violet hue, while bright surfaces still shift towards the Shadow colour.
		vec3 shaded = min(mix(ALBEDO, toon_shadow_color, toon_shadow_mix) / max(ALBEDO, vec3(0.02)) * toon_shadow_level, vec3(1.0));
		// Scalar luminance cap: the shaded colour never ends up brighter than toon_shadow_level times the albedo's luminance,
		// so mid-bright paints don't keep an unintended light shadow after the per-channel clamp.
		const vec3 LUM = vec3(0.2126, 0.7152, 0.0722);
		float k = dot(ALBEDO * shaded, LUM) / max(dot(ALBEDO, LUM), 1e-4);
		shaded *= min(1.0, toon_shadow_level / max(k, 1e-4));
		DIFFUSE_LIGHT += sun * mix(shaded, vec3(1.0), lit);
	} else {
		DIFFUSE_LIGHT += sun * wrapped * ATTENUATION;
	}
}
