// Ink outline: an inverted hull drawn as the next pass of a toon material. Back faces are
// pushed out a fixed number of pixels on screen, so lines stay as thick near and far.
// radial_from_uv: push away from the point (0, UV.y, 0) of the mesh instead of along the
// normal, so flat-shaded meshes keep a closed outline (the tree meshes store each part's
// center height in UV.y).
shader_type spatial;
render_mode unshaded, cull_front, shadows_disabled;

uniform vec4 ink_color : source_color = vec4(0.13, 0.10, 0.14, 1.0);
uniform float width_px = 2.0;
uniform bool radial_from_uv = false;

void vertex() {
	vec3 dir = radial_from_uv ? normalize(VERTEX - vec3(0.0, UV.y, 0.0)) : NORMAL;
	vec4 clip = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	vec2 offset = (PROJECTION_MATRIX * vec4(mat3(MODELVIEW_MATRIX) * dir, 0.0)).xy;
	float len = length(offset);
	if (len > 1e-6) {
		clip.xy += offset / len * width_px * 2.0 / VIEWPORT_SIZE * clip.w;
	}
	POSITION = clip;
}

void fragment() {
	ALBEDO = ink_color.rgb;
}
