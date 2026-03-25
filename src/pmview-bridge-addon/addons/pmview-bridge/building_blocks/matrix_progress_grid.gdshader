shader_type spatial;
render_mode blend_mix, unshaded, cull_disabled;

uniform float cell_states[100]; // 0.0 = off, >0 = on, >1.0 = glow pulse
uniform vec3 on_colour : source_color = vec3(0.024, 0.714, 0.831); // holographic cyan
uniform vec3 off_colour : source_color = vec3(0.067, 0.067, 0.067);
uniform float global_alpha : hint_range(0.0, 1.0) = 1.0;
uniform float grid_gap : hint_range(0.0, 0.1) = 0.04; // gap between cells as fraction of cell

void fragment() {
	vec2 cell_uv = UV * 10.0;
	ivec2 cell_id = ivec2(floor(cell_uv));
	vec2 local_uv = fract(cell_uv);

	cell_id = clamp(cell_id, ivec2(0), ivec2(9));
	int idx = cell_id.y * 10 + cell_id.x;

	float gap_mask = step(grid_gap, local_uv.x) * step(local_uv.x, 1.0 - grid_gap)
	               * step(grid_gap, local_uv.y) * step(local_uv.y, 1.0 - grid_gap);

	float state = cell_states[idx];
	vec3 colour = mix(off_colour, on_colour, step(0.01, state));

	// Glow pulse: state > 1.0 means freshly activated
	float glow = max(state - 1.0, 0.0);
	colour += on_colour * glow * 2.0;

	float cell_alpha = mix(0.15, mix(0.7, 1.0, fract(float(idx) * 0.618)), step(0.01, state));

	ALBEDO = colour;
	ALPHA = gap_mask * cell_alpha * global_alpha;
}
