uniform sampler2D uPage;
uniform sampler2D uScene;
uniform sampler2D uOptical;
uniform sampler2D uShell;
uniform vec4 uBar;
uniform float uPressure;
uniform float uDark;
void main() {
    vec2 p = gl_FragCoord.xy / uDensity;
    vec2 uv = p / uSize;
    vec3 original = texture2D(uPage, uv).rgb;
    vec3 composed = texture2D(uScene, uv).rgb;
    vec4 lens = uPressure > 0.001 ? texture2D(uOptical, uv) * uPressure : vec4(0.0);
    composed = lens.rgb + composed * (1.0 - lens.a);
    float baseAlpha = texture2D(uShell, uv).a;
    float alpha = lens.a + baseAlpha * (1.0 - lens.a);
    vec4 result = vec4(clamp(composed - original * (1.0 - alpha), 0.0, alpha), alpha);
    // Keep Hinge's light border outside the optical input.
    float d = capsule(p, uBar, vec2(1.0)).z;
    float line = (coverage(d) - coverage(d + 1.0)) * (245.0 / 255.0) * (1.0 - uDark);
    gl_FragColor = vec4(vec3(line), line) + result * (1.0 - line);
}
