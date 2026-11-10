package com.hinge.office

import android.graphics.Paint
import androidx.annotation.RequiresApi
import androidx.compose.foundation.layout.Box
import androidx.compose.runtime.Composable
import androidx.compose.runtime.State
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawWithCache
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.geometry.RoundRect
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.drawscope.*
import androidx.compose.ui.graphics.layer.drawLayer
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.IntSize
import kotlin.math.roundToInt

internal fun GlassShape.pixelBounds(density: Float): Rect {
    val halfWidth = width / 2f
    val halfHeight = height / 2f
    return Rect(
        (centerX - halfWidth) * density, (centerY - halfHeight) * density,
        (centerX + halfWidth) * density, (centerY + halfHeight) * density,
    )
}

@RequiresApi(33)
@Composable
internal fun LiveGlassBar(
    shaders: LiveGlassShaders, motion: State<GlassMotion?>, width: Float, surface: Color, dark: Boolean,
    backdrop: GlassBackdrop, inactive: GlassBackdrop, active: GlassBackdrop, barOrigin: Offset,
    modifier: Modifier,
) {
    val pixelDensity = LocalDensity.current.density
    val crop = rememberGraphicsLayer()
    val blurred = rememberGraphicsLayer()
    val prepared = rememberGraphicsLayer()
    val shell = rememberGraphicsLayer()
    val rest = rememberGraphicsLayer()
    val scene = rememberGraphicsLayer()
    val lens = rememberGraphicsLayer()
    val shadowPaints = remember { List(2) { Paint(Paint.ANTI_ALIAS_FLAG) } }
    val bodyBlur = remember(pixelDensity) { navBlurEffect(1.5f * pixelDensity) }
    val frostBlur = remember(pixelDensity) { navBlurEffect(6f * pixelDensity) }
    val bodyEffects = remember(shaders, dark, pixelDensity, width) { mutableMapOf<Size, RenderEffect>() }

    Box(modifier.drawWithCache {
        val frame = motion.value
        if (frame == null || size.width <= 0f || size.height <= 0f) return@drawWithCache onDrawBehind {}
        val geometry = glassGeometry(width, frame)
        val d = density
        val dimensions = IntSize(size.width.roundToInt(), size.height.roundToInt())
        val origin = barOrigin - Offset(GLASS_MARGIN_X * d, GLASS_MARGIN_Y * d)
        fun capsule(rect: Rect, radius: CornerRadius) =
            Path().apply { addRoundRect(RoundRect(rect, radius)) }
        val bar = geometry.bar.pixelBounds(d)
        val barPath = capsule(bar, CornerRadius(bar.height / 2))
        val plate = geometry.lens.pixelBounds(d)
        val platePath = capsule(plate, CornerRadius(minOf(plate.width, plate.height) / 2))
        val artOrigin = Offset(GLASS_MARGIN_X * d, GLASS_MARGIN_Y * d)

        blurred.renderEffect = bodyBlur
        rest.renderEffect = frostBlur
        rest.alpha = geometry.restOpacity
        val showRest = geometry.restOpacity > GLASS_VISIBLE_PRESSURE
        val showLens = geometry.pressure > GLASS_VISIBLE_PRESSURE
        shell.renderEffect = bodyEffects[size] ?: run {
            bodyEffects.clear()
            shaders.shell.configure(size, bar, d, dark, lens = false, pressure = 0f)
            shaders.shell.effect().asComposeRenderEffect().also { bodyEffects[size] = it }
        }
        if (showLens) {
            shaders.lens.configure(size, plate, d, dark, lens = true, pressure = geometry.pressure)
            lens.renderEffect = shaders.lens.effect().asComposeRenderEffect()
            lens.alpha = geometry.pressure
        }

        if (!dark) {
            for ((index, values) in listOf(Triple(4.4f, 1.1f, .033f), Triple(1.1f, .55f, .011f)).withIndex()) {
                shadowPaints[index].apply {
                    color = android.graphics.Color.BLACK
                    setShadowLayer(values.first * d, 0f, values.second * d,
                        Color.Black.copy(alpha = values.third).toArgb())
                }
            }
        }
        fun DrawScope.drawShadows() {
            if (dark) return
            clipPath(barPath, ClipOp.Difference) {
                for (paint in shadowPaints) {
                    drawContext.canvas.nativeCanvas.drawRoundRect(bar.left, bar.top, bar.right, bar.bottom,
                        bar.height / 2, bar.height / 2, paint)
                }
            }
        }

        fun DrawScope.drawLabels(selected: Boolean) {
            translate(artOrigin.x, artOrigin.y) {
                drawLayer(if (selected) active.layer else inactive.layer)
            }
        }

        fun DrawScope.recordGlass(backdropOffset: Offset) {
            crop.record(this, layoutDirection, dimensions) {
                drawRect(surface)
                translate(backdropOffset.x, backdropOffset.y) { drawLayer(backdrop.layer) }
            }
            blurred.record(this, layoutDirection, dimensions) { drawLayer(crop) }
            prepared.record(this, layoutDirection, dimensions) {
                drawLayer(crop)
                drawShadows()
                clipPath(barPath) { drawLayer(blurred) }
            }
            shell.record(this, layoutDirection, dimensions) { drawLayer(prepared) }
            if (showRest) rest.record(this, layoutDirection, dimensions) {
                drawLayer(prepared)
                drawLayer(shell)
            }
            scene.record(this, layoutDirection, dimensions) {
                drawShadows()
                drawLayer(shell)
                clipPath(barPath) {
                    if (showRest) clipPath(platePath) {
                        drawLayer(rest)
                        drawRect((if (dark) Color.White else Color.Black).copy(alpha = 38f / 255f * geometry.restOpacity))
                    }
                    clipPath(platePath, ClipOp.Difference) { drawLabels(false) }
                    clipPath(platePath) { drawLabels(true) }
                }
            }
            if (showLens) {
                lens.record(this, layoutDirection, dimensions) {
                    drawLayer(crop)
                    drawLayer(scene)
                }
            }
        }

        var recordedOffset: Offset? = null
        onDrawBehind {
            if (backdrop.layer.size.width == 0 || inactive.layer.size.width == 0 || active.layer.size.width == 0) return@onDrawBehind
            val backdropOffset = backdrop.origin - origin
            // The hardware display lists refer to live source layers, including during scrolling.
            if (recordedOffset != backdropOffset) {
                recordGlass(backdropOffset)
                recordedOffset = backdropOffset
            }
            drawLayer(scene)
            if (showLens) drawLayer(lens)
            if (!dark) {
                val stroke = d
                drawRoundRect(Color.White.copy(alpha = 245f / 255f),
                    topLeft = Offset(GLASS_MARGIN_X * d + stroke / 2, GLASS_MARGIN_Y * d + stroke / 2),
                    size = androidx.compose.ui.geometry.Size(width * d - stroke, GLASS_HEIGHT * d - stroke),
                    cornerRadius = CornerRadius((GLASS_HEIGHT * d - stroke) / 2), style = Stroke(stroke))
            }
        }
    })
}


// RedNote Compose GlassGpu.kt
private fun navBlurEffect(sigma: Float): BlurEffect {
    val radius = maxOf(.01f, (sigma - .5f) / .57735f)
    return BlurEffect(radius, radius, TileMode.Clamp)
}
