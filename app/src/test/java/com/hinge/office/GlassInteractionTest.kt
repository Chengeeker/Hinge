package com.hinge.office

import androidx.compose.animation.core.TargetBasedAnimation
import androidx.compose.animation.core.VectorConverter
import org.junit.Assert.*
import org.junit.Test

class GlassInteractionTest {
    @Test fun releaseStartsAtTheCurrentPressureAndCrossfadesImmediately() {
        val release = TargetBasedAnimation(glassPressureAnimation(false, false),
            Float.VectorConverter, .8f, 0f)
        assertEquals(240_000_000L, release.durationNanos)
        var previous = .8f
        for (millis in 16..240 step 16) {
            val pressure = release.getValueFromNanos(millis * 1_000_000L)
            val geometry = glassGeometry(240f, GlassMotion(1f, 8f, pressure))
            assertTrue(pressure < previous)
            assertEquals(1f, geometry.restOpacity + geometry.pressure, .0001f)
            previous = pressure
        }
        assertEquals(0f, previous, 0f)
    }

    @Test fun reducedMotionCompletesBothPressureChangesImmediately() {
        for (held in listOf(false, true)) {
            val target = if (held) 1f else 0f
            val animation = TargetBasedAnimation(glassPressureAnimation(held, true),
                Float.VectorConverter, 1f - target, target)
            assertEquals(0L, animation.durationNanos)
            assertEquals(target, animation.getValueFromNanos(0), 0f)
        }
    }
}
