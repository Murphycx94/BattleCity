// Cartoon sky: a smooth gradient from the horizon colour up to the zenith colour, a soft glow around the sun, and
// soft-edged white clouds from thresholded low-frequency noise drifting slowly across a virtual cloud plane.
// The sun direction comes from the scene's DirectionalLight3D (LIGHT0_DIRECTION).
shader_type sky;

#include "res://shaders/common.gdshaderinc"

uniform vec3 sky_top : source_color = vec3(0.31, 0.639, 0.91);        // Palette.SkyTop
uniform vec3 sky_horizon : source_color = vec3(0.867, 0.949, 0.984);  // Palette.SkyHorizon
uniform float gradient_power = 0.55;     // < 1 keeps the pale band near the horizon narrow
uniform vec3 sun_glow : source_color = vec3(1.0, 0.97, 0.86);
uniform float sun_glow_size = 24.0;      // larger = tighter halo
uniform float sun_glow_strength = 0.55;
uniform float sun_disc = 0.9992;         // cos of the disc radius
uniform vec3 cloud_light : source_color = vec3(1.0, 1.0, 1.0);
uniform vec3 cloud_shade : source_color = vec3(0.78, 0.84, 0.95);  // undersides, a pale blue-grey
uniform float cloud_cover = 0.6;         // noise threshold; lower = more cloud
uniform float cloud_softness = 0.04;
uniform float cloud_scale = 1.1;         // noise features per unit of the cloud plane
uniform vec2 cloud_drift = vec2(0.006, 0.002);

// Two octaves only: rounded, puffy blobs rather than wispy fbm detail.
float puff_noise(vec2 p) {
	return value_noise2(p) * 0.68 + value_noise2(p * 2.6 + vec2(5.2, 1.3)) * 0.32;
}

void sky() {
	vec3 dir = normalize(EYEDIR);
	float up = clamp(dir.y, 0.0, 1.0);
	vec3 color = mix(sky_horizon, sky_top, pow(up, gradient_power));

	float toward_sun = max(dot(dir, LIGHT0_DIRECTION), 0.0);
	color = mix(color, sun_glow, pow(toward_sun, sun_glow_size) * sun_glow_strength);
	color = mix(color, vec3(1.0), smoothstep(sun_disc, sun_disc + 0.0004, toward_sun));

	if (!AT_CUBEMAP_PASS && dir.y > 0.0) {   // clouds only in the visible sky; the radiance cubemap is unused
		// Project onto a flat cloud layer; dividing by the height makes clouds shrink and crowd towards the horizon.
		vec2 plane = dir.xz / (dir.y + 0.12) * cloud_scale + TIME * cloud_drift;
		float n = puff_noise(plane);
		float body = smoothstep(cloud_cover, cloud_cover + cloud_softness, n);
		float lit = smoothstep(cloud_cover, cloud_cover + 0.22, puff_noise(plane + vec2(0.06, 0.09)));   // brighter where the cloud is thick, shaded on one side
		vec3 cloud = mix(cloud_shade, cloud_light, lit);
		color = mix(color, cloud, body * smoothstep(0.0, 0.12, dir.y));
	}
	COLOR = color;
}
