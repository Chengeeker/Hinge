package com.hinge.office

import android.content.Context
import android.graphics.RenderEffect as AndroidRenderEffect
import android.graphics.RuntimeShader
import androidx.annotation.RequiresApi
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.Size

@RequiresApi(33)
internal class LiveGlassOptics(source: String) {
    val shader = RuntimeShader(source)

    fun configure(size: Size, shape: Rect, density: Float, dark: Boolean, lens: Boolean, pressure: Float) {
        val optics = glassOptics(density, dark, lens, pressure)
        shader.apply {
            setFloatUniform("uSize", size.width, size.height)
            setFloatUniform("shape", shape.center.x, shape.center.y, shape.width, shape.height)
            setFloatUniform("uGeometryOffset", shape.left, shape.top)
            setFloatUniform("uGeometrySize", shape.width, shape.height)
            setFloatUniform("uGlassColor", optics.tint, optics.tint, optics.tint, optics.tintAlpha)
            setFloatUniform("uOpticalProps", optics.index, optics.dispersion, optics.thickness, 1f)
            setFloatUniform("uLightConfig", optics.light, 0f, optics.saturation)
            setFloatUniform("uLightDirection", -.7071068f, .7071068f)
            setFloatUniform("uWhiten", 0f)
            setFloatUniform("uWhitenGated", 1f)
            setFloatUniform("uPinchStrength", optics.pinch)
            setFloatUniform("uBackgroundFallback", 0f, 0f, 0f, 0f)
            setFloatUniform("uCaptureOffset", 0f, 0f)
            setFloatUniform("uEdgeConfig", 0f, optics.fresnel, density / 3f, 0f)
            setFloatUniform("uPlatformViewMode", 0f)
            setFloatUniform("uBodyMode", 1f)
            setFloatUniform("uTouchPosition", shape.center.x, shape.center.y)
            setFloatUniform("uTouchIntensity", pressure * .125f)
            setFloatUniform("uRimConfig", optics.rimShade, optics.rimLight, .2f)
            setFloatUniform("uLensModel", 0f)
            setFloatUniform("uFrost", 0f, 0f, 0f, 1f)
            setFloatUniform("uBodyShade", 0f)
            setFloatUniform("uNativeEdge", if (lens && !dark) 1f else 0f)
        }
    }

    fun effect(): AndroidRenderEffect = AndroidRenderEffect.createRuntimeShaderEffect(shader, "background")
}

@RequiresApi(33)
internal class LiveGlassShaders(context: Context) {
    private val source = context.assets.open("glass/live_glass.agsl").bufferedReader().use { it.readText() }
    val shell = LiveGlassOptics(source)
    val lens = LiveGlassOptics(source)
}
