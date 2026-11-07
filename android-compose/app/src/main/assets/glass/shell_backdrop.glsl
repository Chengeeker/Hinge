uniform sampler2D uPage;
uniform sampler2D uGeometry;
uniform sampler2D uElevation;
uniform sampler2D uContact;
uniform float uDark;
void main() {
    vec2 p = gl_FragCoord.xy / uDensity;
    vec2 uv = p / uSize;
    float cutout = 1.0 - texture2D(uGeometry, uv).a;
    float a = texture2D(uElevation, (p - vec2(0.0, 1.1)) / uSize).a * 0.033 * cutout;
    float b = texture2D(uContact, (p - vec2(0.0, 0.55)) / uSize).a * 0.011 * cutout;
    float shadow = (1.0 - uDark) * (a + b * (1.0 - a));
    gl_FragColor = vec4(texture2D(uPage, uv).rgb * (1.0 - shadow), shadow);
}
