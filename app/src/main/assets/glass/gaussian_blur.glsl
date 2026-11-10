precision highp float;
uniform sampler2D uSource;
uniform vec2 uSize;
uniform vec2 uStep;
uniform float uSigma;
void main() {
    vec2 uv = gl_FragCoord.xy / uSize;
    vec4 sum = texture2D(uSource, uv);
    float weight = 1.0;
    float inverseVariance = 0.5 / (uSigma * uSigma);
    for (int pair = 0; pair < 192; pair++) {
        float a = float(pair * 2 + 1);
        if (a > ceil(3.0 * uSigma)) break;
        float b = a + 1.0;
        float wa = exp(-a * a * inverseVariance);
        float wb = exp(-b * b * inverseVariance);
        float w = wa + wb;
        vec2 offset = uStep * ((a * wa + b * wb) / max(w, 0.000001));
        sum += (texture2D(uSource, uv + offset) + texture2D(uSource, uv - offset)) * w;
        weight += 2.0 * w;
    }
    gl_FragColor = sum / weight;
}
