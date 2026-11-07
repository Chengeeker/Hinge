package com.hinge.office

import org.junit.Assert.*
import org.junit.Test

class FlutterTypographyTest {
    @Test fun androidThemeSeparatesHeadingsRowsAndSupportingText() {
        val text=hingeTypography(0)
        val roles=listOf(text.displayLarge,text.displayMedium,text.displaySmall,text.headlineLarge,text.headlineMedium,text.headlineSmall,text.titleLarge,text.titleMedium,text.titleSmall,text.bodyLarge,text.bodyMedium,text.bodySmall,text.labelLarge,text.labelMedium,text.labelSmall)
        val sizes=listOf(57f,45f,36f,32f,28f,24f,22f,18f,14f,16f,14f,12f,14f,12f,11f)
        val heights=listOf(63.84f,52.2f,43.92f,40f,36.12f,32f,28f,24f,20f,24f,20f,17f,20f,16f,16f)
        val weights=listOf(500,500,500,500,500,700,700,600,600,500,400,400,600,500,600)
        roles.forEachIndexed { i, style ->
            assertEquals(sizes[i],style.fontSize.value,0f)
            assertEquals(heights[i],style.lineHeight.value,.001f)
            assertEquals(weights[i],style.fontWeight?.weight)
        }
        assertEquals(-.25f,text.displayLarge.letterSpacing.value,0f)
        assertEquals(.5f,text.labelSmall.letterSpacing.value,0f)
        assertEquals(0f,text.bodyMedium.letterSpacing.value,0f)
        assertEquals(300,hingeTypography(1).bodyMedium.fontWeight?.weight)
        assertEquals(800,hingeTypography(2).titleLarge.fontWeight?.weight)
        assertEquals(900,hingeTypography(4).titleLarge.fontWeight?.weight)
    }
}
