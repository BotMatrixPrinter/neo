// Cel shading for the plane: three flat light bands and a thin bright rim.
shader_type spatial;
render_mode cull_back, specular_disabled;

uniform vec4 albedo : source_color = vec4(1.0);
uniform float shade_dark = 0.45;
uniform float shade_mid = 0.75;
uniform vec4 rim_color : source_color = vec4(1.0, 0.97, 0.88, 1.0);

void fragment() {
	ALBEDO = albedo.rgb;
	ROUGHNESS = 1.0;
	float rim = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	EMISSION = rim_color.rgb * albedo.rgb * step(0.82, rim) * 0.25;
}

void light() {
	float ndl = dot(NORMAL, LIGHT) * ATTENUATION;
	float band = mix(shade_dark, shade_mid, smoothstep(-0.05, 0.0, ndl));
	band = mix(band, 1.0, smoothstep(0.45, 0.5, ndl));
	DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR / PI * band;
}
