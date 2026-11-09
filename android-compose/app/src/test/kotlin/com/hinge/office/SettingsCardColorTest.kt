package com.hinge.office

import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SettingsCardColorTest {
    @Test fun keepsTheWallpaperRoleWhenItSeparatesFromThePage() {
        val card = Color(0xFFE9E3EA)
        val scheme = lightColorScheme(background = Color(0xFFFFF8FF), surfaceContainerHigh = card)
        assertEquals(card, scheme.settingsCardColor())
    }

    @Test fun preservesAVisibleCardWhenRolesCollapseOrThePageOverridesTheTheme() {
        for (page in listOf(Color(0xFFFFF8FF), Color(0xFF111215), Color.Black)) {
            val theme = if (page.luminance() > .5f) lightColorScheme() else darkColorScheme()
            val scheme = theme.copy(surfaceContainerHigh = page)
            val card = scheme.settingsCardColor(page)
            assertTrue(kotlin.math.abs(card.luminance() - page.luminance()) >= (page.luminance() + .05f) * .10f)
            assertEquals(1f, card.alpha, 0f)
        }
    }
}
