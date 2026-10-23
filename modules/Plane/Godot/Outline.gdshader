// Ink outline (inverted hull): back faces pushed out along the normal by a few pixels,
// drawn flat ink. Used as the toon material's next pass.
shader_type spatial;
render_mode unshaded, cull_front, depth_draw_opaque;

uniform vec4 ink : source_color = vec4(0.08, 0.06, 0.05, 1.0);
uniform float width_pixels = 3.0;
uniform float max_width_meters = 0.08;

void vertex() {
	vec4 view = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	vec3 normal = normalize((MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	vec4 clip = PROJECTION_MATRIX * view;
	vec2 dir = (PROJECTION_MATRIX * vec4(normal, 0.0)).xy;
	float len = length(dir);
	if (len > 1e-5) {
		// Constant on screen near the camera, never thicker than max_width_meters in the world.
		float pixels = min(width_pixels, max_width_meters * len / max(clip.w, 1e-3) * VIEWPORT_SIZE.x * 0.5);
		clip.xy += dir / len * pixels * 2.0 / VIEWPORT_SIZE * clip.w;
	}
	POSITION = clip;
}

void fragment() {
	ALBEDO = ink.rgb;
}
