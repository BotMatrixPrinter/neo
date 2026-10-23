// Cel-shaded lighting shared by the World's materials: the sun's light falls in two hard
// bands (shadow, half-lit, lit) instead of a smooth gradient. Ambient fills the shadows.
// Other modules may copy this approach; they must not include this file.
void light() {
	float ndl = dot(NORMAL, LIGHT) * ATTENUATION;
	float bands = smoothstep(0.0, 0.04, ndl) * 0.55 + smoothstep(0.38, 0.42, ndl) * 0.45;
	DIFFUSE_LIGHT += LIGHT_COLOR / PI * bands;
}
