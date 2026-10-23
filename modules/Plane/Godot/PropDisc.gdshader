// The prop disc: a cartoon blur of the spinning propeller, a thin round mesh in front of the cowl.
// Semi-transparent, soft radial streaks, no outline. `fade` 0..1 (alpha) is set by PlaneView.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec4 tint : source_color = vec4(0.93, 0.86, 0.72, 1.0);
uniform vec4 streak_color : source_color = vec4(0.55, 0.36, 0.18, 1.0);
uniform float fade : hint_range(0.0, 1.0) = 0.0;
uniform float radius = 1.42;
uniform float lobes = 2.0;
uniform float rays = 9.0;

varying vec2 local;

void vertex() {
	local = VERTEX.xz; // the disc is a flat cylinder: its face is the mesh's XZ plane
}

void fragment() {
	float r = length(local) / radius;
	float angle = atan(local.y, local.x);
	// Soft smears where the blades sweep, a lighter ring near the tips, faint at the hub.
	float smear = 0.5 + 0.5 * cos(angle * lobes);
	smear = smear * smear;
	float ray = pow(0.5 + 0.5 * cos(angle * rays + r * 2.0), 6.0);
	smear = clamp(smear * 0.8 + ray * 0.35, 0.0, 1.0);
	float ring = smoothstep(0.7, 0.85, r) * (1.0 - smoothstep(0.88, 0.98, r));
	vec3 color = mix(tint.rgb, streak_color.rgb, smear * 0.6);
	color = mix(color, vec3(1.0), ring * 0.35);
	float edge = 1.0 - smoothstep(0.9, 1.0, r);
	float body = mix(0.55, 1.0, smear) * smoothstep(0.08, 0.25, r);
	ALBEDO = color;
	ALPHA = fade * edge * clamp(body + ring * 0.4, 0.0, 1.0);
}
