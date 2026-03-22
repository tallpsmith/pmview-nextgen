shader_type spatial;
render_mode unshaded, cull_disabled, blend_add;

uniform vec4 beam_colour : source_color = vec4(0.3, 0.75, 0.97, 0.06);
uniform float scan_line_count : hint_range(5.0, 50.0) = 20.0;
uniform float scan_line_speed : hint_range(0.0, 5.0) = 1.5;
uniform float scan_line_intensity : hint_range(0.0, 1.0) = 0.15;
uniform float edge_fade : hint_range(0.0, 0.5) = 0.3;
uniform float beam_height : hint_range(1.0, 100.0) = 15.0;

varying float v_height_ratio;

void vertex() {
	// Pass normalised height (0 = floor, 1 = ceiling) to fragment
	v_height_ratio = clamp(VERTEX.y / beam_height, 0.0, 1.0);
}

void fragment() {
	// Base beam colour with vertical fade (brighter near source, dimmer at top)
	float vertical_fade = mix(1.0, 0.3, v_height_ratio);

	// Rising scan lines
	float scan = sin((UV.y - TIME * scan_line_speed) * scan_line_count * TAU);
	scan = scan * 0.5 + 0.5;  // normalise to 0-1
	float scan_contribution = scan * scan_line_intensity;

	// Edge fade — soften the beam edges horizontally
	float edge = smoothstep(0.0, edge_fade, UV.x) * smoothstep(0.0, edge_fade, 1.0 - UV.x);

	ALBEDO = beam_colour.rgb;
	ALPHA = beam_colour.a * vertical_fade * edge * (1.0 + scan_contribution);
}
