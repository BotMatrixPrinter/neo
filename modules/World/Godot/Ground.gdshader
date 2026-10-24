// Toon material of the ground: a patchwork of fields (greens and golden yellows) with dark
// hedges and ink lines between them on the low land, grassier hills, rocky faceted mountain
// slopes with a snow or pale rock cap (on the range's ring, through the uniforms: no fields but
// grass and forest green on the foothills, rock higher up, soft strata, snow from about 1400 m),
// all painted from the world position and normal; and the
// water (river, lakes) where UV.x < 0: bright blue with soft foam at the shore and animated
// ripples. UV.x is set by WorldView: height above the water on land, minus the depth on water.
shader_type spatial;
render_mode specular_disabled;

#include "res://modules/World/Godot/Toon.gdshaderinc"

uniform float snow_line = 192.0;
uniform float pale_line = 150.0;
// Height (m) of a rock stratum and how far (m) the snow and pale rock lines wander: larger on the wall ring.
uniform float strata_size = 17.0;
uniform float line_wander = 1.0;
// How much darker the dark rock strata are (1: as dark as ROCK_DARK), and how much higher snow starts on steep rock (m).
uniform float strata_contrast = 1.0;
uniform float snow_steep = 6.0;
// How much of the low land is patchwork fields (0: meadow only), up to which height (m) a darker forest
// green mottles the grass, and the heights (m) over which meadow, rock (and rock on steep ground) take over.
uniform float fields_share = 1.0;
uniform float forest_line = 0.0;
uniform vec2 meadow_band = vec2(14.0, 40.0);
uniform vec2 rock_band = vec2(48.0, 80.0);
uniform vec2 steep_rock_band = vec2(28.0, 48.0);
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
const vec3 MEADOW = vec3(0.45, 0.72, 0.32);
const vec3 FOREST = vec3(0.24, 0.50, 0.25);
const vec3 ROCK = vec3(0.68, 0.60, 0.52);
const vec3 ROCK_DARK = vec3(0.56, 0.49, 0.44);
const vec3 PALE_ROCK = vec3(0.80, 0.77, 0.71);
const vec3 SNOW = vec3(0.96, 0.98, 1.0);
const vec3 WATER = vec3(0.16, 0.56, 0.90);
const vec3 WATER_SHALLOW = vec3(0.36, 0.78, 0.95);
const vec3 WATER_LIGHT = vec3(0.70, 0.92, 1.0);
const vec3 FOAM = vec3(0.95, 0.98, 1.0);
const vec3 BANK = vec3(0.90, 0.84, 0.58);

varying vec3 world_pos;
varying vec3 world_normal;
varying float shore;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	shore = UV.x;
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

	// Hills: the patchwork fades into meadow as the land rises (on the range: meadow only, with
	// patches of darker forest green that thin out toward the forest line).
	float y = world_pos.y;
	vec3 meadow = MEADOW * (0.9 + 0.2 * noise(p * 0.01));
	float forest = smoothstep(0.45, 0.6, noise(p * 0.004) + 0.5 * noise(p * 0.017) - 0.6 * y / max(forest_line, 1.0)) * step(1.0, forest_line);
	meadow = mix(meadow, FOREST * (0.9 + 0.2 * noise(p * 0.03)), forest);
	color = mix(meadow, color, fields_share);
	color = mix(color, meadow, smoothstep(meadow_band.x, meadow_band.y, y) * 0.65 * fields_share);

	// Mountains: rock on the high and steep ground, banded in cartoon strata, drawn with flat
	// facets (the triangle's own normal) for a low-poly look; snow or pale rock on the tops.
	float steep = 1.0 - smoothstep(0.62, 0.8, normalize(world_normal).y);
	float rock = max(smoothstep(rock_band.x, rock_band.y, y), steep * smoothstep(steep_rock_band.x, steep_rock_band.y, y));
	float strata = step(0.5, fract(y / strata_size + 0.6 * noise(p * 0.012 * 17.0 / strata_size) + 0.004 * dot(p, vec2(0.6, 0.8))));
	color = mix(color, mix(ROCK, ROCK_DARK, (1.0 - strata) * strata_contrast) * (0.92 + 0.16 * noise(p * 0.04)), rock);
	color = mix(color, PALE_ROCK, smoothstep(pale_line - 8.0, pale_line + 8.0, y + line_wander * 14.0 * noise(p * 0.02)));
	color = mix(color, SNOW, smoothstep(snow_line - 2.0, snow_line + 2.0, y + line_wander * 16.0 * noise(p * 0.018 / line_wander) - snow_steep * steep));
	vec3 facet = normalize(cross(dFdy(VERTEX), dFdx(VERTEX)));
	NORMAL = normalize(mix(NORMAL, facet, rock));

	// Shores: a sandy strip, then the water: deeper blue offshore, soft foam along the shore
	// that breathes in and out, gentle drifting ripple lines, an ink line at the water's edge.
	float aa = max(fwidth(shore), 0.01);
	color = mix(color, BANK, 1.0 - smoothstep(0.7, 0.7 + aa, shore));
	float depth = -shore;
	float water = smoothstep(-aa, 0.0, depth);
	float t = TIME;
	vec3 water_color = mix(WATER_SHALLOW, WATER, smoothstep(0.0, 1.6, depth));
	float wave = sin(p.x * 0.07 + 2.2 * sin(p.y * 0.031 + t * 0.35) + t * 0.9)
		* sin(p.y * 0.055 - t * 0.6 + 1.8 * sin(p.x * 0.027 - t * 0.25));
	float ripples = smoothstep(0.55, 0.8, wave) * (1.0 - smoothstep(600.0, 1500.0, view_distance));
	water_color = mix(water_color, WATER_LIGHT, ripples * 0.55);
	float foam_edge = 0.35 + 0.18 * sin(t * 1.2 + p.x * 0.045 + p.y * 0.035);
	float foam = 1.0 - smoothstep(foam_edge * 0.5, foam_edge + 0.25, depth);
	water_color = mix(water_color, FOAM, foam * 0.85);
	color = mix(color, water_color, water);
	color = mix(color, INK, line(abs(shore), 0.04) * 0.6 * ink_fade);
	if (depth > 0.0) {
		NORMAL = normalize((VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
	}

	ALBEDO = color;
}
