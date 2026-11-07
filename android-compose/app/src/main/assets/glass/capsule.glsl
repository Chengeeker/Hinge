// Adapted from liquid_glass_geometry_blended.frag and liquid_glass_render.frag.
// Copyright 2026 Sebastian Degenaar. SPDX-License-Identifier: MIT
precision highp float;
uniform vec2 uSize;
uniform float uDensity;

// xy = center, zw = size before the jelly transform. The SDF is evaluated
// directly at display resolution, so the matte and optics share one contour.
vec3 rounded(vec2 p, vec4 rect, vec2 scale, float radiusInset) {
    vec2 h = rect.zw * 0.5;
    float radius = max(0.0, min(h.x, h.y) - radiusInset);
    vec2 local = (p - rect.xy) / scale;
    vec2 q = abs(local) - h + radius;
    vec2 edge = max(q, 0.0);
    float distance = length(edge) + min(max(q.x, q.y), 0.0) - radius;
    vec2 gradient = sign(local) * (length(edge) > 0.0001
        ? normalize(edge) : (q.x > q.y ? vec2(1, 0) : vec2(0, 1)));
    vec2 normal = gradient / scale;
    return vec3(normalize(normal + vec2(0.00001)), distance / max(length(normal), 0.0001));
}

vec3 capsule(vec2 p, vec4 rect, vec2 scale) {
    return rounded(p, rect, scale, 0.0);
}

float coverage(float distance) {
    return 1.0 - smoothstep(-0.65, 0.65, distance * uDensity);
}

