shader_type canvas_item;
render_mode unshaded;
uniform vec2 logical_size = vec2(432.0, 864.0);
uniform float corner_radius = 25.0;
void fragment() {
    // CanvasItem fragment COLOR already includes the texture and vertex modulation.
    vec4 sample_color = COLOR;
    vec2 q = abs(UV * logical_size - logical_size * 0.5) - (logical_size * 0.5 - vec2(corner_radius));
    float distance_to_edge = length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - corner_radius;
    float aa = max(fwidth(distance_to_edge), 0.05);
    sample_color.a *= 1.0 - smoothstep(-aa * 0.5, aa * 0.5, distance_to_edge);
    COLOR = sample_color;
}
