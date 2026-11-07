package com.hinge.office

import org.junit.Assert.*
import org.junit.Test

class FlutterIndicatorsTest {
    @Test fun indeterminateCyclesRetainFlutterTimingAndBounds(){
        assertEquals(listOf(0f to 0f,0f to 0f),flutterLinearSegments(0f))
        assertEquals(1f,flutterLinearSegments(750f)[0].second,0f)
        assertEquals(1f,flutterLinearSegments(1083f)[0].first,0f)
        assertEquals(0f,flutterLinearSegments(1000f)[1].second,0f)
        assertEquals(1f,flutterLinearSegments(1567f)[1].second,0f)
        assertEquals(flutterLinearSegments(375f),flutterLinearSegments(2175f))
        for(time in 0..1800 step 15)for((start,end)in flutterLinearSegments(time.toFloat()))assertTrue(start in 0f..1f&&end in start..1f)
        assertEquals(-90f,flutterCircularArc(0f).first,0f)
        assertEquals(270f,flutterCircularArc(666.5f).second,.001f)
        assertTrue(flutterCircularArc(1332.99f).second<.1f)
    }
}
