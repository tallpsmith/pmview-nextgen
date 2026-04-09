shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;

uniform float dot_spacing : hint_range(0.5, 10.0) = 2.0;
uniform float dot_size : hint_range(0.005, 0.5) = 0.08;
uniform vec4 dot_color : source_color = vec4(0.0, 0.8, 0.3, 1.0);
uniform float fade_distance : hint_range(1.0, 50.0) = 18.0;
uniform float speed : hint_range(0.0, 1.0) = 0.0;
uniform float streak_angle : hint_range(0.0, 6.28) = 0.0;

varying vec2 world_xz;

void vertex() {
    vec4 world_pos = MODEL_MATRIX * vec4(VERTEX, 1.0);
    world_xz = world_pos.xz;
}

void fragment() {
    // Tile into repeating grid cells in world space
    vec2 cell = fract(world_xz / dot_spacing) - 0.5;

    // Rotate cell by streak_angle, then compress along rotated Y to stretch dots
    float sa = sin(streak_angle);
    float ca = cos(streak_angle);
    vec2 rotated = vec2(ca * cell.x + sa * cell.y, -sa * cell.x + ca * cell.y);
    rotated.y /= max(1.0 - speed * 0.95, 0.05);

    // Chebyshev distance from cell centre — square/pixel SDF (stretches into streaks)
    float dist = max(abs(rotated.x), abs(rotated.y));
    float dot = 1.0 - smoothstep(dot_size - 0.003, dot_size + 0.003, dist);

    // Radial fade from world origin
    float dist_from_centre = length(world_xz);
    float fade = 1.0 - smoothstep(fade_distance * 0.5, fade_distance, dist_from_centre);

    ALBEDO = dot_color.rgb;
    ALPHA = dot * fade * dot_color.a;
}
