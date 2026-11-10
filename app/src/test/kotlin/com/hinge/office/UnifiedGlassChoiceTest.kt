package com.hinge.office

import android.graphics.Bitmap
import android.graphics.Canvas
import android.view.inspector.WindowInspector
import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.*
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35], application = android.app.Application::class, qualifiers = "zh-rCN-w360dp-h800dp-mdpi")
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class UnifiedGlassChoiceTest {
    val compose = createAndroidComposeRule<ComponentActivity>()
    private val register = org.junit.rules.TestRule { base, _ -> object : org.junit.runners.model.Statement() {
        override fun evaluate() {
            val context = androidx.test.core.app.ApplicationProvider.getApplicationContext<android.content.Context>()
            org.robolectric.Shadows.shadowOf(context.packageManager).addActivityIfNotPresent(android.content.ComponentName(context, ComponentActivity::class.java))
            base.evaluate()
        }
    } }
    @get:Rule val rules: org.junit.rules.TestRule = org.junit.rules.RuleChain.outerRule(register).around(compose)

    @Test fun referenceLayoutAndFontSizesIgnoreHostTypography() = checkChoice(1f)

    @Test
    @Config(qualifiers = "zh-rCN-w320dp-h800dp-mdpi")
    fun narrowLargeFontKeepsRightRadioCenteredAndAllActionsVisible() = checkChoice(1.5f)

    private fun checkChoice(scale: Float) {
        var choice by mutableStateOf(false)
        var dark by mutableStateOf(false)
        compose.setContent {
            // Deliberately hostile host text size: the shared UI must retain RedNote's metrics.
            val type = Typography(bodyMedium = androidx.compose.ui.text.TextStyle(fontSize = 28.sp, lineHeight = 42.sp, fontWeight = FontWeight.Normal))
            MaterialTheme(colorScheme = if (dark) darkColorScheme() else lightColorScheme(), typography = type) {
                Box(Modifier.fillMaxSize()) { NavigationGlassChoice(choice, fontScale = scale, hapticsEnabled = false) { choice = it } }
            }
        }
        val entry = compose.onNodeWithTag("glass-choice-entry")
        assertTrue(entry.fetchSemanticsNode().boundsInRoot.height >= 64f)
        entry.performClick()
        fun textNode(text: String) = compose.onNode(hasText(text) and hasAnyAncestor(isDialog()), useUnmergedTree = true)
        fun textStyle(text: String, expected: Float) {
            val layouts = mutableListOf<TextLayoutResult>()
            textNode(text).performSemanticsAction(SemanticsActions.GetTextLayoutResult) { assertTrue(it(layouts)) }
            assertEquals(expected * scale, layouts.single().layoutInput.style.fontSize.value, .01f)
        }
        textStyle("底栏玻璃样式", 24f)
        textStyle("通透磨砂", 16f)
        textStyle("经典玻璃", 16f)
        textStyle("底色更厚实，可读性更高", 14f)
        textStyle("更多背景透出，对比度更低", 14f)
        val frosted = compose.onNodeWithTag("glass-choice-row-false")
        val classic = compose.onNodeWithTag("glass-choice-row-true")
        frosted.assertIsSelected()
        assertTrue(frosted.fetchSemanticsNode().boundsInRoot.top < classic.fetchSemanticsNode().boundsInRoot.top)
        for (clear in listOf(false, true)) {
            val row = compose.onNodeWithTag("glass-choice-row-$clear").fetchSemanticsNode().boundsInRoot
            val radio = compose.onNodeWithTag("glass-choice-radio-$clear", useUnmergedTree = true).fetchSemanticsNode().boundsInRoot
            assertEquals(row.center.y, radio.center.y, .5f)
            assertEquals(row.right, radio.right, .5f)
            assertTrue(radio.left > row.center.x)
        }
        val result = JSONObject()
        for (night in listOf(false, true)) {
            compose.runOnIdle { dark = night }
            val metrics = JSONObject()
            for (label in listOf("底栏玻璃样式", "通透磨砂", "经典玻璃", "底色更厚实，可读性更高", "更多背景透出，对比度更低", "取消")) {
                val b = textNode(label).fetchSemanticsNode().boundsInRoot
                metrics.put(label, listOf(b.left, b.top, b.right, b.bottom).joinToString(","))
            }
            compose.runOnUiThread {
                val root = WindowInspector.getGlobalWindowViews().last { it.width > 0 && it.height > 0 }
                val bitmap = Bitmap.createBitmap(root.width, root.height, Bitmap.Config.ARGB_8888)
                root.draw(Canvas(bitmap))
                java.io.File("build/parity/unified-choice-$scale-$night.png").apply { parentFile.mkdirs() }.outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
                bitmap.recycle()
            }
            result.put(night.toString(), metrics)
        }
        java.io.File("build/parity/unified-choice-$scale.json").apply { parentFile.mkdirs() }.writeText(result.toString(2))
        compose.onNodeWithText("取消").assertIsDisplayed().performClick()
        entry.performClick()
        compose.onNodeWithTag("glass-choice-row-false").assertIsSelected()
        compose.onNodeWithTag("glass-choice-row-true").performClick()
        compose.runOnIdle { assertTrue(choice) }
        entry.performClick()
        compose.onNodeWithTag("glass-choice-row-true").assertIsSelected()
        compose.onNodeWithTag("glass-choice-row-false").performClick()
        compose.runOnIdle { assertFalse(choice) }
    }
}
