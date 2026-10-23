// Toon material of the ground: a patchwork of fields (greens and golden yellows) with dark
// hedges and ink lines between them, and the winding river, all painted from the world
// position. The river curve must match WorldView.RiverX.
shader_type spatial;
render_mode specular_disabled;

#include "res://modules/World/Godot/Toon.gdshaderinc"

uniform float river_half_width = 22.0;
uniform float ink_fade_start = 500.0;
uniform float ink_fade_end = 1600.0;

const vec3 FIELDS[7] = vec3[7](
	vec3(0.53, 0.76, 0.36),
	vec3(0.66, 0.82, 0.42),
	vec3(0.42, 0.66, 0.31),
	vec3(0.93, 0.79, 0.38),
	vec3(0.96, 0.87, 0.54),
	vec3(0.75, 0.81, 0.42),
	vec3(0.89, 0.70, 0.35)
);
const vec3 HEDGE = vec3(0.30, 0.52, 0.25);
const vec3 INK = vec3(0.13, 0.10, 0.14);
const vec3 WATER = vec3(0.30, 0.66, 0.82);
const vec3 WATER_LIGHT = vec3(0.62, 0.88, 0.95);
const vec3 BANK = vec3(0.90, 0.84, 0.58);

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

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

// Same curve as WorldView.RiverX / RiverSlope.
float river_x(float z) {
	return 260.0 * sin(z * 0.0011 + 0.6) + 120.0 * sin(z * 0.0029 + 2.1) + 40.0 * sin(z * 0.0071);
}

float river_slope(float z) {
	return 0.286 * cos(z * 0.0011 + 0.6) + 0.348 * cos(z * 0.0029 + 2.1) + 0.284 * cos(z * 0.0071);
}

float river_distance(vec2 p) {
	float s = river_slope(p.y);
	return abs(p.x - river_x(p.y)) / sqrt(1.0 + s * s);
}

// Thin line of about width meters (at least ~1 pixel, anti-aliased) where d is near 0.
float line(float d, float width) {
	float aa = fwidth(d);
	return 1.0 - smoothstep(width, width + max(aa, 0.01), d);
}

void fragment() {
	vec2 p = world_pos.xz;
	float view_distance = length(VERTEX);
	float ink_fade = 1.0 - smoothstep(ink_fade_start, ink_fade_end, view_distance);

	// Fields: a rotated, gently wobbling grid; some cells split in two.
	float a = 0.35;
	vec2 q = mat2(vec2(cos(a), sin(a)), vec2(-sin(a), cos(a))) * p;
	q += 22.0 * vec2(sin(q.y * 0.0041), sin(q.x * 0.0037));
	vec2 size = vec2(190.0, 130.0);
	vec2 cell = floor(q / size);
	vec2 local = q - cell * size;
	vec2 id = cell;
	float split = hash(cell + 7.0);
	if (split < 0.35) {
		float half_x = size.x * 0.5;
		id.x += step(half_x, local.x) * 0.5;
		local.x = mod(local.x, half_x);
		size.x = half_x;
	} else if (split < 0.6) {
		float half_y = size.y * 0.5;
		id.y += step(half_y, local.y) * 0.5;
		local.y = mod(local.y, half_y);
		size.y = half_y;
	}
	float pick = hash(id * 1.37 + 3.1);
	vec3 color = FIELDS[int(pick * 6.999)];
	// Crop rows and a soft painterly wash.
	float row_angle = hash(id + 11.0) * 3.14159;
	float rows = sin(dot(q, vec2(cos(row_angle), sin(row_angle))) * 1.6);
	color *= 1.0 + 0.025 * rows * (1.0 - smoothstep(100.0, 400.0, view_distance));
	color *= 0.9 + 0.2 * noise(p * 0.006) + 0.06 * noise(p * 0.05);

	float edge = min(min(local.x, size.x - local.x), min(local.y, size.y - local.y));
	color = mix(color, HEDGE, line(edge, 1.4));
	color = mix(color, INK, line(abs(edge - 1.6), 0.3) * 0.8 * ink_fade);

	// River: sandy banks, water with light ripple lines, an ink line along each bank.
	float rd = river_distance(p);
	float bank = river_half_width + 6.0;
	color = mix(color, BANK, 1.0 - smoothstep(bank, bank + max(fwidth(rd), 0.01), rd));
	float water = 1.0 - smoothstep(river_half_width, river_half_width + max(fwidth(rd), 0.01), rd);
	float ripples = step(0.93, sin(p.y * 0.12 + sin(p.x * 0.05) * 2.0 + rd * 0.4)) * step(rd, river_half_width - 4.0);
	vec3 water_color = mix(WATER, WATER_LIGHT, ripples * 0.7 + 0.25 * (1.0 - smoothstep(0.0, river_half_width, rd)));
	color = mix(color, water_color, water);
	color = mix(color, INK, line(abs(rd - river_half_width), 0.5) * ink_fade);

	ALBEDO = color;
}
