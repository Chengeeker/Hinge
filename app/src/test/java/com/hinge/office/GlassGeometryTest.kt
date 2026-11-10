package com.hinge.office

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class GlassGeometryTest {
    private val rest = GlassMotion(1f, 0f, 0f)

    @Test fun liveRestingPlateFitsOneSlotAtEveryTab() {
        val width = 213.33333f
        val density = 3.5f
        for (slot in 0..2) {
            val geometry = glassGeometry(width, rest.copy(position = slot.toFloat()))
            val bar = geometry.bar.pixelBounds(density)
            val plate = geometry.lens.pixelBounds(density)
            val tabWidth = (width - 8f) / 3f
            assertEquals(tabWidth * density, plate.width, .001f)
            assertEquals((GLASS_MARGIN_X + 4f + tabWidth * slot) * density, plate.left, .001f)
            assertEquals(48f * density, plate.height, .001f)
            assertTrue(plate.left >= bar.left && plate.right <= bar.right)
        }
    }

    @Test fun activeLensBloomsBeyondShellAndDropsRestFrost() {
        val idle = glassGeometry(232f, rest)
        val active = glassGeometry(232f, rest.copy(pressure = 1f))
        assertEquals(48f, idle.lens.height, .001f)
        assertEquals(24f + idle.lens.width * .06f, active.lens.width - idle.lens.width, .001f)
        assertEquals(48f * 1.06f + 16f, active.lens.height, .001f)
        assertEquals(idle.bar, active.bar)
        assertTrue(active.lens.height > active.bar.height)
        assertEquals(1f, idle.restOpacity, .001f)
        assertEquals(0f, active.restOpacity, .001f)
    }

    @Test fun horizontalMotionOnlyDistortsThePressedLens() {
        val held = rest.copy(position = 0f, pressure = 1f)
        val still = glassGeometry(293f, held)
        val moving = glassGeometry(293f, held.copy(velocity = -10f))
        assertTrue(moving.lens.width < still.lens.width)
        assertTrue(moving.lens.height > still.lens.height)
        assertTrue(moving.lens.pixelBounds(1f).left > 0f)
        assertEquals(still, glassGeometry(293f, held.copy(velocity = Float.NaN)))
        assertEquals(glassGeometry(293f, rest), glassGeometry(293f, rest.copy(velocity = 10f)))
    }

    @Test fun dragClampsEdgesAndOnlyFlingsFromTheCurrentSlot() {
        assertEquals(0f, glassDragPosition(-24f, 240f), .001f)
        assertEquals(1f, glassDragPosition(120f, 240f), .001f)
        assertEquals(2f, glassDragPosition(300f, 240f), .001f)
        assertEquals(1, glassReleaseTab(.9f, 799f, 1, true))
        assertEquals(2, glassReleaseTab(.9f, 800f, 1, true))
        assertEquals(0, glassReleaseTab(.9f, -800f, 1, true))
        assertEquals(1, glassReleaseTab(.9f, 800f, 1, false))
        assertEquals(2, glassReleaseTab(2f, -1000f, 1, true))
    }

    @Test fun springSettlesAndRetainsVelocityWhenRetargeted() {
        val spring = GlassSpring(0f)
        spring.step(2f, .05f, .35f, .85f)
        assertTrue(spring.velocity > 0f)
        val position = spring.value
        spring.step(0f, .001f, .35f, .85f)
        assertTrue(spring.value > position)
        repeat(80) { spring.step(0f, .016f, .35f, .85f) }
        assertEquals(0f, spring.value, .001f)
        assertEquals(0f, spring.velocity, .001f)
    }
}
