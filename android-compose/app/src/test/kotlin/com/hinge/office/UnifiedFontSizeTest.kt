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
import androidx.compose.ui.unit.*
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.*

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35], application = android.app.Application::class, qualifiers = "zh-rCN-w360dp-h800dp-mdpi")
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class UnifiedFontSizeTest {
    private val compose = createAndroidComposeRule<ComponentActivity>()
    private val register = org.junit.rules.TestRule { base, _ -> object : org.junit.runners.model.Statement() {
        override fun evaluate() {
            val context = androidx.test.core.app.ApplicationProvider.getApplicationContext<android.content.Context>()
            org.robolectric.Shadows.shadowOf(context.packageManager).addActivityIfNotPresent(android.content.ComponentName(context, ComponentActivity::class.java))
            base.evaluate()
        }
    } }
    @get:Rule val rules: org.junit.rules.TestRule = org.junit.rules.RuleChain.outerRule(register).around(compose)

    @Test fun dragChangesOnlyLocalPercentageUntilReleaseAndAllReferenceStepsAreAvailable() {
        var stored by mutableFloatStateOf(1f)
        var writes = 0
        compose.setContent { MaterialTheme { FontSizeSetting(stored) { stored = it; writes++ } } }
        val slider = compose.onNodeWithContentDescription("字体大小")
        slider.performTouchInput { down(center); moveTo(androidx.compose.ui.geometry.Offset(width - 1f, center.y), 100) }
        assertEquals(1f, stored); assertEquals(0, writes)
        compose.onNodeWithTag("font-size-label").assertTextEquals("150%")
        slider.performTouchInput { up() }
        compose.runOnIdle { assertEquals(1.5f, stored); assertEquals(1, writes) }
        for (percent in 85..150 step 5) {
            slider.performSemanticsAction(SemanticsActions.SetProgress) { it(percent / 100f) }
            compose.onNodeWithTag("font-size-label").assertTextEquals("$percent%")
            assertEquals(percent / 100f, stored, .001f)
        }
        compose.onNodeWithText("应用字体大小").assertDoesNotExist()
    }
    @Test fun referenceLayout() = render(1f)
    @Test @Config(qualifiers = "zh-rCN-w320dp-h800dp-mdpi") fun narrowLargeFontLayout() = render(1.5f)
    private fun render(scale: Float) {
        var dark by mutableStateOf(false)
        compose.setContent { MaterialTheme(colorScheme = if (dark) darkColorScheme() else lightColorScheme()) {
            Box(Modifier.fillMaxSize().padding(16.dp)) { FontSizeSetting(1f, fontScale = scale) {} }
        } }
        for (night in listOf(false,true)) {
            compose.runOnIdle { dark = night }
            compose.onNodeWithTag("font-size-label").assertTextEquals("100%").assertIsDisplayed()
            val results = mutableListOf<androidx.compose.ui.text.TextLayoutResult>()
            compose.onNodeWithTag("font-size-label").performSemanticsAction(SemanticsActions.GetTextLayoutResult) { it(results) }
            assertEquals(16f * scale, results.single().layoutInput.style.fontSize.value, .01f)
            compose.runOnUiThread {
                val root = WindowInspector.getGlobalWindowViews().last { it.width > 0 && it.height > 0 }
                val bitmap = Bitmap.createBitmap(root.width,root.height,Bitmap.Config.ARGB_8888)
                root.draw(Canvas(bitmap))
                java.io.File("build/parity/font-size-$scale-$night.png").apply { parentFile.mkdirs() }.outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG,100,it) }
                bitmap.recycle()
            }
        }
    }
}
