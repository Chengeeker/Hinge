uniform sampler2D uShell;
uniform sampler2D uRestBlur;
uniform sampler2D uUnselected;
uniform sampler2D uSelected;
uniform vec4 uBar;
uniform vec4 uLens;
uniform float uRestOpacity;
uniform float uDark;

void main() {
    vec2 p = gl_FragCoord.xy / uDensity;
    vec2 uv = p / uSize;
    vec3 under = texture2D(uShell, uv).rgb;
    float barMask = coverage(capsule(p, uBar, vec2(1.0)).z);
    float lensMask = coverage(capsule(p, uLens, vec2(1.0)).z);
    if (lensMask * barMask > 0.0 && uRestOpacity > 0.001) {
        vec3 frosted = mix(under, texture2D(uRestBlur, uv).rgb, uRestOpacity);
        vec3 backing = mix(frosted, vec3(uDark), (38.0 / 255.0) * uRestOpacity);
        under = mix(under, backing, lensMask * barMask);
    }
    vec4 glyph = mix(texture2D(uUnselected, uv), texture2D(uSelected, uv), lensMask) * barMask;
    gl_FragColor = vec4(glyph.rgb + under * (1.0 - glyph.a), 1.0);
}
