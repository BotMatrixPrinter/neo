// Toon material of the low-poly trees: vertex colors (tinted per tree by the MultiMesh).
shader_type spatial;
render_mode specular_disabled;

#include "res://modules/World/Godot/Toon.gdshaderinc"

void fragment() {
	ALBEDO = COLOR.rgb;
}
