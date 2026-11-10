package com.hinge.office

import org.junit.Assert.assertEquals
import org.junit.Test

class GlassOpticsTest {
    @Test fun shellUsesRedNoteMaterialInBothThemes() {
        val light = glassOptics(3f, false, false)
        val dark = glassOptics(3f, true, false)
        assertEquals(1.16f, light.index, .0001f)
        assertEquals(1f, light.saturation, 0f)
        assertEquals(1f, light.tint, 0f)
        assertEquals(240f / 255f, light.tintAlpha, .0001f)
        assertEquals(0f, dark.tint, 0f)
        assertEquals(.4f, dark.tintAlpha, .0001f)
        assertEquals(.12f, dark.rimLight, .0001f)
    }

    @Test fun glassThicknessTracksPixelDensity() {
        for (density in listOf(1f, 2f, 3f, 3.5f)) {
            assertEquals(20f / 3f, glassOptics(density, false, true).thickness / density, .0001f)
            assertEquals(10f / 3f, glassOptics(density, false, false).thickness / density, .0001f)
        }
    }

    @Test fun reducedPressureChangesRefractionAndRimLighting() {
        val halfway = glassOptics(3f, false, true, .5f)
        assertEquals(10f, halfway.thickness, .0001f)
        assertEquals(1.09f, halfway.index, .0001f)
        assertEquals(.0125f, halfway.dispersion, .0001f)
        assertEquals(.525f, halfway.pinch, .0001f)
        assertEquals(.06f, halfway.rimShade, .0001f)
        assertEquals(.175f, halfway.rimLight, .0001f)
    }

    @Test fun darkLensKeepsItsWhiteTintAndSkipsTheLightRim() {
        val dark = glassOptics(3f, true, true)
        assertEquals(1f, dark.tint, 0f)
        assertEquals(38f / 255f, dark.tintAlpha, .0001f)
        assertEquals(0f, dark.rimShade, 0f)
        assertEquals(0f, dark.rimLight, 0f)
        assertEquals(1.5f, dark.saturation, 0f)
    }
}
