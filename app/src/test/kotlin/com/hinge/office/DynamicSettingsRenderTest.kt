package com.hinge.office

import android.app.Application
import android.graphics.Bitmap
import android.graphics.Canvas
import android.view.View
import androidx.activity.ComponentActivity
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Rule
import org.junit.Test
import org.junit.rules.RuleChain
import org.junit.rules.TestRule
import org.junit.runner.RunWith
import org.junit.runners.model.Statement
import org.robolectric.Robolectric
import org.robolectric.RobolectricTestRunner
import org.robolectric.Shadows
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35], application = Application::class, qualifiers = "zh-rCN-w360dp-h800dp-mdpi")
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class DynamicSettingsRenderTest {
    private val compose = createAndroidComposeRule<ComponentActivity>()
    private val register = TestRule { base, _ -> object : Statement() {
        override fun evaluate() {
            val context = androidx.test.core.app.ApplicationProvider.getApplicationContext<android.content.Context>()
            Shadows.shadowOf(context.packageManager).addActivityIfNotPresent(
                android.content.ComponentName(context, ComponentActivity::class.java)
            )
            base.evaluate()
        }
    } }
    @get:Rule val rules: TestRule = RuleChain.outerRule(register).around(compose)

    @Test fun homeAndPersonalizationUseTheSameVisibleCardInDynamicThemes() {
        val model = HingeModel(Robolectric.buildActivity(MainActivity::class.java).get(), NativeApi())
        val open = mutableStateOf(false)
        var page = 0
        var card = 0
        compose.setContent {
            HingeTheme(model) {
                val colors = MaterialTheme.colorScheme
                SideEffect {
                    page = colors.surface.toArgb()
                    card = colors.settingsCardColor(colors.surface).toArgb()
                }
                if (open.value) OriginalAppearance(model) { open.value = false }
                else OriginalSettings(model) { open.value = true }
            }
        }
        for ((mode, pureBlack) in listOf("light" to false, "dark" to false, "dark" to true)) {
            val tag = if (pureBlack) "black" else mode
            compose.runOnIdle {
                model.settings = mapOf(
                    "themePreference" to mode, "dynamicColorEnabled" to true,
                    "pureBlackDarkMode" to pureBlack, "floatingCapsuleNavigation" to false
                )
            }
            for (appearance in listOf(false, true)) {
                compose.runOnIdle { open.value = appearance }
                val label = if (appearance) "跟随系统" else "个性化"
                val y = compose.onNode(hasText(label) and hasClickAction()).fetchSemanticsNode().boundsInRoot.center.y.toInt()
                compose.runOnUiThread {
                    val content = compose.activity.findViewById<View>(android.R.id.content)
                    val frame = Bitmap.createBitmap(content.width, content.height, Bitmap.Config.ARGB_8888)
                    try {
                        content.draw(Canvas(frame))
                        assertNotEquals(tag, page, card)
                        assertEquals(tag, page, frame.getPixel(8, y))
                        assertEquals(tag, card, frame.getPixel(frame.width - 20, y))
                        val screen = if (appearance) "appearance" else "home"
                        File("build/parity/settings-card-dynamic-$tag-$screen.png").apply { parentFile.mkdirs() }
                            .outputStream().use { frame.compress(Bitmap.CompressFormat.PNG, 100, it) }
                    } finally {
                        frame.recycle()
                    }
                }
            }
        }
    }
}
