package com.hinge.office

import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.exp
import kotlin.math.roundToInt
import kotlin.math.sin
import kotlin.math.sqrt

internal const val GLASS_HEIGHT = 56f
internal const val GLASS_MARGIN_X = 48f
internal const val GLASS_MARGIN_Y = 28f

internal const val GLASS_HOLD_MILLIS = 220L

internal const val GLASS_DRAG_SLOP_DP = 18f
internal const val GLASS_RELEASE_MILLIS = 240
internal const val GLASS_VISIBLE_PRESSURE = .001f

internal data class GlassMotion(val position: Float, val velocity: Float, val pressure: Float)

internal data class GlassShape(
    val centerX: Float, val centerY: Float, val width: Float, val height: Float,
)

internal data class GlassGeometry(
    val bar: GlassShape,
    val lens: GlassShape,
    val restOpacity: Float,
    val pressure: Float,
)

internal fun glassGeometry(width: Float, motion: GlassMotion): GlassGeometry {
    val pressure = motion.pressure.coerceIn(0f, 1f)
    val slot = (width - 8f) / 3f
    val cx = GLASS_MARGIN_X + width / 2f
    val cy = GLASS_MARGIN_Y + GLASS_HEIGHT / 2f
    val jelly = jellyScale(motion.velocity, pressure)
    val expansion = 1f + .06f * pressure
    return GlassGeometry(
        bar = GlassShape(cx, cy, width, GLASS_HEIGHT),
        lens = GlassShape(
            cx + (motion.position - 1f) * slot,
            cy,
            (slot * expansion + 24f * pressure) * jelly[0],
            ((GLASS_HEIGHT - 8f) * expansion + 16f * pressure) * jelly[1],
        ),
        restOpacity = 1f - pressure,
        pressure = pressure,
    )
}

internal fun glassDragPosition(x: Float, width: Float): Float =
    (x / width * 3f - .5f).coerceIn(0f, 2f)

internal fun glassReleaseTab(position: Float, speedDp: Float, selected: Int, dragging: Boolean): Int {
    val slot = position.roundToInt().coerceIn(0, 2)
    return if (dragging && abs(speedDp) >= 800f && slot == selected) {
        (selected + if (speedDp > 0f) 1 else -1).coerceIn(0, 2)
    } else slot
}

// RedNote Compose GlassMotion.kt
internal fun jellyScale(velocity: Float, pressure: Float = 1f): FloatArray {
    val distortion =
        if (velocity.isFinite()) (abs(velocity) / 10).coerceIn(0f, 1f) * .8f * .7f * pressure
        else 0f
    return floatArrayOf(1 - distortion * .5f, 1 + distortion * .3f)
}

// Exact step of Flutter's duration-and-bounce spring, preserving velocity
// when a finger redirects it. A long suspended frame cannot destabilize it.
internal class GlassSpring(initial: Float) {
    var value = initial
        private set
    var velocity = 0f
        private set

    fun snap(target: Float) {
        value = target
        velocity = 0f
    }

    fun step(target: Float, seconds: Float, duration: Float, damping: Float) {
        val displacement = value - target
        if (abs(displacement) < .0005f && abs(velocity) < .008f) {
            value = target
            velocity = 0f
            return
        }
        val omega = 6.2831853f / duration
        val decay = omega * damping
        val envelope = exp(-decay * seconds)
        if (damping == 1f) {
            val rate = velocity + decay * displacement
            val curve = displacement + rate * seconds
            value = target + envelope * curve
            velocity = envelope * (rate - decay * curve)
        } else {
            val frequency = omega * sqrt(1f - damping * damping)
            val coefficient = (velocity + decay * displacement) / frequency
            val cosine = cos(frequency * seconds)
            val sine = sin(frequency * seconds)
            val curve = displacement * cosine + coefficient * sine
            value = target + envelope * curve
            velocity = envelope * (
                frequency * (-displacement * sine + coefficient * cosine) - decay * curve
            )
        }
    }
}
