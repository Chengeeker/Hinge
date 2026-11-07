package com.hinge.office

// Navigation material from RedNote Compose GlassGpu.kt and GlassView.kt.
internal data class GlassOptics(
    val thickness: Float, val index: Float, val dispersion: Float,
    val light: Float, val saturation: Float, val tint: Float, val tintAlpha: Float,
    val rimShade: Float, val rimLight: Float, val fresnel: Float, val pinch: Float,
)

internal fun glassOptics(density: Float, dark: Boolean, indicator: Boolean, pressure: Float = 1f): GlassOptics {
    val fade = if (indicator) pressure.coerceIn(.001f, 1f) else 1f
    val tintAlpha = if (indicator) {
        if (dark) 38f / 255 else 24f / 255
    } else if (dark) .4f else 240f / 255
    val rimLight = if (dark) {
        if (indicator) 0f else .12f
    } else if (indicator) .35f else 1f
    return GlassOptics(
        thickness = (if (indicator) 20f * fade else 10f) * density / 3f,
        index = if (indicator) 1f + .18f * fade else 1.16f,
        dispersion = if (indicator) .025f * fade else .008f,
        light = (if (indicator) 2f else .25f) * fade,
        saturation = if (indicator) 1.5f else 1f,
        tint = if (indicator == dark) 1f else 0f,
        tintAlpha = tintAlpha * fade,
        rimShade = if (indicator && !dark) .12f * fade else 0f,
        rimLight = rimLight * fade,
        fresnel = fade,
        pinch = if (indicator) .7f * (1f - (1f - fade) * (1f - fade)) else 0f,
    )
}
