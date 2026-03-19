shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;

uniform float dot_spacing : hint_range(0.5, 10.0) = 2.0;
uniform float dot_radius : hint_range(0.005, 0.5) = 0.06;
uniform vec4 dot_color : source_color = vec4(0.0, 0.8, 0.3, 1.0);
uniform float fade_distance : hint_range(1.0, 50.0) = 18.0;

varying vec2 world_xz;

void vertex() {
    vec4 world_pos = MODEL_MATRIX * vec4(VERTEX, 1.0);
    world_xz = world_pos.xz;
}

void fragment() {
    // Tile into repeating grid cells in world space
    vec2 cell = fract(world_xz / dot_spacing) - 0.5;

    // Distance from cell centre — circle SDF
    float dist = length(cell);
    float dot = 1.0 - smoothstep(dot_radius - 0.005, dot_radius + 0.005, dist);

    // Radial fade from world origin
    float dist_from_centre = length(world_xz);
    float fade = 1.0 - smoothstep(fade_distance * 0.5, fade_distance, dist_from_centre);

    ALBEDO = dot_color.rgb;
    ALPHA = dot * fade * dot_color.a;
}
