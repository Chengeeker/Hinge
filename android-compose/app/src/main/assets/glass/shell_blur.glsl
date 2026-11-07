uniform sampler2D uBackdrop;
uniform sampler2D uBlurred;
uniform vec4 uBar;
void main() {
    vec2 p = gl_FragCoord.xy / uDensity;
    vec2 uv = p / uSize;
    float mask = coverage(capsule(p, uBar, vec2(1.0)).z);
    gl_FragColor = vec4(mix(texture2D(uBackdrop, uv).rgb,
        texture2D(uBlurred, uv).rgb, mask), 1.0);
}
