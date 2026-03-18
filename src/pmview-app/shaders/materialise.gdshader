shader_type spatial;
render_mode unshaded;

uniform float materialise : hint_range(0.0, 1.0) = 0.0;
uniform vec4 wire_color : source_color = vec4(0.228, 0.525, 1.0, 0.2);
uniform vec4 solid_color_top : source_color = vec4(1.0, 0.0, 0.43, 1.0);
uniform vec4 solid_color_bottom : source_color = vec4(0.228, 0.525, 1.0, 1.0);
uniform float glow_pulse : hint_range(0.0, 1.0) = 0.0;

void fragment() {
    float t = clamp(UV.y, 0.0, 1.0);
    vec4 solid = mix(solid_color_bottom, solid_color_top, t);
    vec4 base = mix(wire_color, solid, materialise);
    float emission_strength = glow_pulse * 2.0 + materialise * 0.5;
    ALBEDO = base.rgb;
    ALPHA = mix(wire_color.a, 1.0, materialise);
    EMISSION = base.rgb * emission_strength;
}
