package com.hinge.office

import android.os.SystemClock
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.snap
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.util.VelocityTracker
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import kotlin.math.PI
import kotlin.math.abs

internal fun glassPressureAnimation(longPressed: Boolean, reduceMotion: Boolean) = when {
    reduceMotion -> snap<Float>()
    longPressed -> spring(dampingRatio = .85f, stiffness = (4 * PI * PI / (.30 * .30)).toFloat())
    else -> tween(GLASS_RELEASE_MILLIS, easing = FastOutSlowInEasing)
}

internal fun Modifier.glassTabGestures(
    width: Dp, layout: LayoutDirection, selectedSlot: () -> Int,
    onDrag: (Float?) -> Unit, onLongPress: (Boolean) -> Unit,
    onGestureStarted: (Boolean) -> Unit, selectSlot: (Int) -> Unit,
) = pointerInput(width, layout) {
    val slop = GLASS_DRAG_SLOP_DP.dp.toPx()
    awaitEachGesture {
        val down = awaitFirstDown(requireUnconsumed = false, pass = PointerEventPass.Initial)
        if (down.isConsumed) return@awaitEachGesture
        down.consume()
        val tracker = VelocityTracker()
        tracker.addPosition(down.uptimeMillis, down.position)
        var dragging = false
        var lastX = down.position.x
        val deadline = down.uptimeMillis + GLASS_HOLD_MILLIS

        try {
            while (true) {
                val event = if (!dragging) {
                    withTimeoutOrNull((deadline - SystemClock.uptimeMillis()).coerceAtLeast(1L)) {
                        awaitPointerEvent(PointerEventPass.Initial)
                    } ?: run {
                        onLongPress(true)
                        dragging = true
                        onDrag(glassDragPosition(lastX, size.width.toFloat()))
                        onGestureStarted(true)
                        continue
                    }
                } else awaitPointerEvent(PointerEventPass.Initial)
                val change = event.changes.firstOrNull { it.id == down.id } ?: break
                if (change.isConsumed || event.changes.any { it.id != down.id && it.pressed }) break
                lastX = change.position.x
                tracker.addPosition(change.uptimeMillis, change.position)
                val delta = change.position - down.position
                if (!dragging && delta.getDistanceSquared() > slop * slop) {
                    if (abs(delta.x) <= abs(delta.y)) {
                        change.consume()
                        break
                    }
                    dragging = true
                    onGestureStarted(false)
                }
                val target = glassDragPosition(lastX, size.width.toFloat())
                if (dragging) onDrag(target)
                change.consume()
                if (!change.pressed) {
                    val speed = if (dragging) tracker.calculateVelocity().x / this.density else 0f
                    val selected = selectedSlot()
                    val slot = glassReleaseTab(target, speed, selected, dragging)
                    if (!dragging || slot != selected) selectSlot(slot)
                    break
                }
            }
        } finally {
            onLongPress(false)
            onDrag(null)
        }
    }
}
