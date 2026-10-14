// Shared noise for the cartoon shaders: cheap hash-based value noise and fbm.
// The noise is for looks only; anything the game logic must agree with comes from C# (GroundCover mirrors hash12 and
// value_noise2 float for float, so keep the two in step).

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

// Smooth noise in [0, 1], one feature per unit.
float value_noise2(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = hash12(i);
	float b = hash12(i + vec2(1.0, 0.0));
	float c = hash12(i + vec2(0.0, 1.0));
	float d = hash12(i + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float value_noise3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float n000 = hash13(i);
	float n100 = hash13(i + vec3(1.0, 0.0, 0.0));
	float n010 = hash13(i + vec3(0.0, 1.0, 0.0));
	float n110 = hash13(i + vec3(1.0, 1.0, 0.0));
	float n001 = hash13(i + vec3(0.0, 0.0, 1.0));
	float n101 = hash13(i + vec3(1.0, 0.0, 1.0));
	float n011 = hash13(i + vec3(0.0, 1.0, 1.0));
	float n111 = hash13(i + vec3(1.0, 1.0, 1.0));
	float x00 = mix(n000, n100, u.x);
	float x10 = mix(n010, n110, u.x);
	float x01 = mix(n001, n101, u.x);
	float x11 = mix(n011, n111, u.x);
	return mix(mix(x00, x10, u.y), mix(x01, x11, u.y), u.z);
}

// Four octaves, still in [0, 1].
float fbm2(vec2 p) {
	float sum = 0.0;
	float weight = 0.5;
	for (int i = 0; i < 4; i++) {
		sum += value_noise2(p) * weight;
		p = p * 2.03 + vec2(17.1, 9.3);
		weight *= 0.5;
	}
	return sum / 0.9375;
}

float fbm3(vec3 p) {
	float sum = 0.0;
	float weight = 0.5;
	for (int i = 0; i < 3; i++) {
		sum += value_noise3(p) * weight;
		p = p * 2.03 + vec3(17.1, 9.3, 4.7);
		weight *= 0.5;
	}
	return sum / 0.875;
}
