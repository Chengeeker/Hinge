package com.hinge.office

import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import androidx.compose.runtime.remember
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.input.pointer.motionEventSpy
import androidx.compose.ui.platform.LocalView

internal class TapHaptics {
    private var tracking = false
    private var moved = false
    private var startX = 0f
    private var startY = 0f

    fun cancel(): Boolean = tracking.also { tracking = false }

    fun onTouch(event: MotionEvent, density: Float): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                val tool = event.getToolType(0)
                tracking = tool == MotionEvent.TOOL_TYPE_FINGER || tool == MotionEvent.TOOL_TYPE_STYLUS
                moved = false
                startX = event.x
                startY = event.y
            }
            MotionEvent.ACTION_MOVE, MotionEvent.ACTION_UP -> if (tracking) {
                val limit = 12f * density
                fun exceeded(x: Float, y: Float): Boolean {
                    val dx = x - startX
                    val dy = y - startY
                    return dx * dx + dy * dy > limit * limit
                }
                for (index in 0 until event.historySize) {
                    if (moved) break
                    moved = exceeded(event.getHistoricalX(index), event.getHistoricalY(index))
                }
                moved = moved || exceeded(event.x, event.y)
                if (event.actionMasked == MotionEvent.ACTION_UP) {
                    cancel()
                    return !moved
                }
            }
            MotionEvent.ACTION_POINTER_DOWN, MotionEvent.ACTION_CANCEL -> cancel()
        }
        return false
    }
}

internal val LocalHingeTapFeedbackEnabled = staticCompositionLocalOf<() -> Boolean> { { false } }

// Dialogs and popups receive their own Android events outside Activity.dispatchTouchEvent.
internal fun Modifier.hingeWindowTapFeedback(): Modifier = composed {
    val view = LocalView.current
    val enabled = LocalHingeTapFeedbackEnabled.current
    val tracker = remember(view) { TapHaptics() }
    motionEventSpy { event ->
        if (tracker.onTouch(event, view.resources.displayMetrics.density) && enabled()) {
            view.performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
        }
    }
}
