package com.hinge.office

import android.graphics.Bitmap
import android.graphics.Canvas
import android.view.inspector.WindowInspector
import androidx.activity.ComponentActivity
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.text.TextStyle
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
class UnifiedFontWeightTest {
    val compose = createAndroidComposeRule<ComponentActivity>()
    private val register = org.junit.rules.TestRule { base, _ -> object : org.junit.runners.model.Statement() {
        override fun evaluate() {
            val context = androidx.test.core.app.ApplicationProvider.getApplicationContext<android.content.Context>()
            org.robolectric.Shadows.shadowOf(context.packageManager).addActivityIfNotPresent(android.content.ComponentName(context, ComponentActivity::class.java))
            base.evaluate()
        }
    } }
    @get:Rule val rules: org.junit.rules.TestRule = org.junit.rules.RuleChain.outerRule(register).around(compose)

    @Test fun draggingOnlyPreviewsUntilReleaseAndResetKeepsSavedValue() {
        var custom by mutableStateOf(true)
        var delta by mutableIntStateOf(100)
        var writes = 0
        compose.setContent {
            MaterialTheme {
                FontWeightSetting(custom, delta, "RedNote", systemDelta = 150, hapticsEnabled = false,
                    onReset = { custom = false }, onCommit = { delta = it; custom = true; writes++ })
            }
        }
        val slider = compose.onNodeWithContentDescription("字体粗细")
        fun previewWeight(): Int {
            val result = mutableListOf<TextLayoutResult>()
            compose.onNodeWithTag("font-weight-preview").performSemanticsAction(SemanticsActions.GetTextLayoutResult) { it(result) }
            return result.single().layoutInput.style.fontWeight!!.weight
        }
        assertEquals(500, previewWeight())
        slider.performTouchInput {
            down(androidx.compose.ui.geometry.Offset(width * 2f / 3f, center.y))
            moveTo(androidx.compose.ui.geometry.Offset(width - 1f, center.y), 100)
        }
        assertEquals(100, delta)
        assertEquals(0, writes)
        assertEquals(700, previewWeight())
        slider.performTouchInput { up() }
        compose.runOnIdle { assertEquals(300, delta); assertEquals(1, writes) }
        compose.onNodeWithText("重置").performClick()
        compose.runOnIdle { assertFalse(custom); assertEquals(300, delta) }
        compose.onNodeWithText("重置").assertIsNotEnabled()
        compose.onNodeWithTag("font-weight-label").assertTextEquals("跟随系统")
        assertEquals(550, previewWeight())
        slider.performSemanticsAction(SemanticsActions.SetProgress) { it(-300f) }
        compose.runOnIdle { assertTrue(custom); assertEquals(-300, delta); assertEquals(2, writes) }
        assertEquals(100, previewWeight())
        compose.onNodeWithText("选择字体粗细").assertDoesNotExist()
    }

    @Test fun oldWeightLevelsAndNewSliderExtremesKeepTheirMeaning() {
        assertEquals(listOf(0, -100, 50, 100, 200), (0..4).map { hingeLegacyWeightDelta(it) })
        assertEquals(500, hingeTypography(2).bodyMedium.fontWeight!!.weight)
        assertEquals(100, hingeTypography(0, adjustment = -300).bodyMedium.fontWeight!!.weight)
        assertEquals(700, hingeTypography(0, adjustment = 300).bodyMedium.fontWeight!!.weight)
    }

    @Test fun referenceLayoutIgnoresHostTextSizes() = checkLayout(1f)
    @Test @Config(qualifiers = "zh-rCN-w320dp-h800dp-mdpi")
    fun narrowScreenWithLargeFontsKeepsAllControlsReachable() = checkLayout(1.5f)

    private fun checkLayout(scale: Float) {
        var dark by mutableStateOf(false)
        compose.setContent {
            MaterialTheme(colorScheme = if (dark) darkColorScheme() else lightColorScheme(),
                typography = Typography(bodyMedium = TextStyle(fontSize = 28.sp, lineHeight = 42.sp))) {
                Box(Modifier.fillMaxSize().padding(16.dp)) {
                    FontWeightSetting(false, 100, "RedNote", fontScale = scale, systemDelta = 0,
                        hapticsEnabled = false, onReset = {}, onCommit = {})
                }
            }
        }
        val result = JSONObject()
        for (night in listOf(false, true)) {
            compose.runOnIdle { dark = night }
            val metrics = JSONObject()
            for ((tag, size) in listOf("font-weight-label" to 16f, "font-weight-preview" to 14f)) {
                val node = compose.onNodeWithTag(tag).assertIsDisplayed()
                val bounds = node.fetchSemanticsNode().boundsInRoot
                val layout = mutableListOf<TextLayoutResult>()
                node.performSemanticsAction(SemanticsActions.GetTextLayoutResult) { it(layout) }
                assertEquals(size * scale, layout.single().layoutInput.style.fontSize.value, .01f)
                assertTrue(bounds.left >= 16f && bounds.right <= (if (scale == 1f) 344f else 304f))
                metrics.put(tag, listOf(bounds.left, bounds.top, bounds.right, bounds.bottom).joinToString(","))
            }
            compose.onNodeWithText("重置").assertIsDisplayed()
            compose.onNodeWithContentDescription("字体粗细").assertIsDisplayed()
            compose.runOnUiThread {
                val root = WindowInspector.getGlobalWindowViews().last { it.width > 0 && it.height > 0 }
                val bitmap = Bitmap.createBitmap(root.width, root.height, Bitmap.Config.ARGB_8888)
                root.draw(Canvas(bitmap))
                java.io.File("build/parity/font-weight-$scale-$night.png").apply { parentFile.mkdirs() }.outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
                bitmap.recycle()
            }
            result.put(night.toString(), metrics)
        }
        java.io.File("build/parity/font-weight-$scale.json").apply { parentFile.mkdirs() }.writeText(result.toString(2))
    }
}
