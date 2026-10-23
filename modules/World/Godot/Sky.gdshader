// Bright stylized sky: a soft gradient from a pale warm horizon to deep blue, a sun halo,
// and fluffy two-tone cartoon clouds.
shader_type sky;

uniform vec3 top_color : source_color = vec3(0.20, 0.48, 0.88);
uniform vec3 horizon_color : source_color = vec3(0.80, 0.90, 0.96);
uniform vec3 below_color : source_color = vec3(0.80, 0.90, 0.96);
uniform vec3 cloud_light : source_color = vec3(1.0, 0.99, 0.95);
uniform vec3 cloud_shade : source_color = vec3(0.70, 0.78, 0.92);
uniform float cloud_cover = 0.52;

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
		mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm(vec2 p) {
	float v = 0.0;
	float amp = 0.5;
	for (int i = 0; i < 4; i++) {
		v += amp * noise(p);
		p = p * 2.03 + vec2(17.1, 9.3);
		amp *= 0.5;
	}
	return v;
}

void sky() {
	vec3 d = EYEDIR;
	float h = d.y;
	vec3 color = mix(horizon_color, top_color, smoothstep(0.0, 0.55, h));
	color = mix(below_color, color, smoothstep(-0.02, 0.0, h));

	if (LIGHT0_ENABLED) {
		float sun = dot(d, LIGHT0_DIRECTION);
		color += vec3(1.0, 0.85, 0.6) * pow(max(sun, 0.0), 24.0) * 0.35;
		color = mix(color, vec3(1.0, 0.97, 0.85) * 2.0, smoothstep(0.9985, 0.999, sun));
	}

	if (h > 0.0) {
		vec2 uv = d.xz / (h + 0.12) * 0.9 + vec2(TIME * 0.004, 0.0);
		float n = fbm(uv);
		float cloud = smoothstep(cloud_cover, cloud_cover + 0.03, n);
		// Shade the underside: where the noise grows toward the sun-facing top it is lit.
		float lit = smoothstep(0.0, 0.02, fbm(uv + vec2(0.0, 0.18)) - n + 0.04);
		vec3 cloud_color = mix(cloud_shade, cloud_light, lit);
		float fade = smoothstep(0.02, 0.2, h);
		color = mix(color, cloud_color, cloud * fade);
	}
	COLOR = color;
}
