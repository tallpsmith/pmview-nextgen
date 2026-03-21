shader_type spatial;
render_mode unshaded, cull_front;

uniform vec4 outline_colour : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void fragment() {
	ALBEDO = outline_colour.rgb;
	ALPHA = outline_colour.a;
}
