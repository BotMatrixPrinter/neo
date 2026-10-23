// Cel shading for the propeller blades (see Toon.gdshaderinc), fading out with `alpha` as the prop disc fades in.
shader_type spatial;
render_mode cull_back, specular_disabled, depth_draw_always;

#define FADE
#include "res://modules/Plane/Godot/Toon.gdshaderinc"
