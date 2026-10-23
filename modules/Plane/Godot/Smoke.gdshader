// Dark smoke puffs (CPUParticles3D, blended over what is behind them): a soft round puff
// billboarded toward the camera, colored by the particle's color. Particle slots not yet
// spawned (zero scale, or no color) are dropped, so they never show as black squares.
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;

uniform sampler2D puff : source_color, filter_linear;

varying float dead;

void vertex() {
	float scale = length(MODEL_MATRIX[0].xyz);
	dead = scale < 1e-4 || COLOR.a < 1e-3 ? 1.0 : 0.0;
	mat4 world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = VIEW_MATRIX * world * mat4(vec4(scale, 0.0, 0.0, 0.0), vec4(0.0, scale, 0.0, 0.0), vec4(0.0, 0.0, scale, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
}

void fragment() {
	if (dead > 0.5)
		discard;
	ALBEDO = COLOR.rgb;
	ALPHA = texture(puff, UV).a * COLOR.a;
}
