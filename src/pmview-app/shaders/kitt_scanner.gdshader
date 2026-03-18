shader_type canvas_item;

uniform float sweep_position : hint_range(-0.4, 1.4) = -0.4;
uniform float sweep_width : hint_range(0.1, 0.6) = 0.4;
uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform vec4 color_left : source_color = vec4(1.0, 0.0, 0.43, 1.0);
uniform vec4 color_mid : source_color = vec4(0.514, 0.22, 0.925, 1.0);
uniform vec4 color_right : source_color = vec4(0.228, 0.525, 1.0, 1.0);

void fragment() {
	float dist = abs(UV.x - sweep_position);
	float glow = smoothstep(sweep_width, 0.0, dist);
	float t = clamp((UV.x - sweep_position + sweep_width) / (sweep_width * 2.0), 0.0, 1.0);
	vec4 sweep_color = mix(mix(color_left, color_mid, t), color_right, t);
	COLOR = vec4(sweep_color.rgb, glow * intensity);
}
