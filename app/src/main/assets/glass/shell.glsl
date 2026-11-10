uniform sampler2D uPrepared;
uniform sampler2D uOptical;
uniform sampler2D uBackdrop;
void main() {
    vec2 p = gl_FragCoord.xy / uDensity;
    vec2 uv = p / uSize;
    vec3 page = texture2D(uPrepared, uv).rgb;
    vec4 glass = texture2D(uOptical, uv);
    float shadow = texture2D(uBackdrop, uv).a;
    gl_FragColor = vec4(glass.rgb + page * (1.0 - glass.a),
        glass.a + shadow * (1.0 - glass.a));
}
