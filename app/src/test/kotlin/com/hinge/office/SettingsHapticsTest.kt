package com.hinge.office

import android.view.*
import androidx.activity.ComponentActivity
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.unit.dp
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.*
import org.robolectric.shadows.ShadowViewGroup

@Implements(ViewGroup::class)
class SettingsPulseShadow : ShadowViewGroup() {
    companion object { val pulses = mutableListOf<Int>() }
    @Implementation override fun performHapticFeedback(feedbackConstant: Int): Boolean {
        pulses += feedbackConstant
        return super.performHapticFeedback(feedbackConstant)
    }
}

class SettingsTouchActivity : ComponentActivity() {
    internal val taps = TapHaptics()
    var feedbackEnabled = true
    override fun dispatchTouchEvent(event: MotionEvent): Boolean = taps.dispatchTouch(event,
        resources.displayMetrics.density, { feedbackEnabled },
        { window.decorView.performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK) }) { super.dispatchTouchEvent(event) }
}

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [28, 35], application = android.app.Application::class,
    qualifiers = "zh-rCN-w360dp-h800dp-mdpi", shadows = [SettingsPulseShadow::class])
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class SettingsHapticsTest {
    private val compose = createAndroidComposeRule<SettingsTouchActivity>()
    private val register = org.junit.rules.TestRule { base, _ -> object : org.junit.runners.model.Statement() {
        override fun evaluate() {
            val context = androidx.test.core.app.ApplicationProvider.getApplicationContext<android.content.Context>()
            org.robolectric.Shadows.shadowOf(context.packageManager).addActivityIfNotPresent(android.content.ComponentName(context, SettingsTouchActivity::class.java))
            base.evaluate()
        }
    } }
    @get:Rule val rules: org.junit.rules.TestRule = org.junit.rules.RuleChain.outerRule(register).around(compose)
    private lateinit var contentView: View
    private val pulses get() = SettingsPulseShadow.pulses

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable private fun Host(content: @Composable () -> Unit) {
        contentView = LocalView.current
        ProvideHingeTapFeedback({ compose.activity.feedbackEnabled }, compose.activity.taps::componentFeedback) {
            MaterialTheme { androidx.compose.runtime.CompositionLocalProvider(LocalRippleConfiguration provides null) {
                Surface(Modifier.fillMaxSize()) { Box(Modifier.fillMaxSize().padding(top = 64.dp)) { content() } }
            } }
        }
    }

    // Compose performTouchInput bypasses Activity dispatch. Send actual Android events here.
    private fun touch(node: SemanticsNodeInteraction, drag: Boolean = false, cancel: Boolean = false, rightEdge: Boolean = false) {
        val bounds = node.fetchSemanticsNode().boundsInRoot
        var point = Offset.Zero
        compose.runOnUiThread {
            val origin = IntArray(2); contentView.getLocationOnScreen(origin)
            val decor = IntArray(2); compose.activity.window.decorView.getLocationOnScreen(decor)
            val localPoint = if (rightEdge) Offset(bounds.right - 32f, bounds.center.y) else bounds.center
            point = localPoint + Offset((origin[0] - decor[0]).toFloat(), (origin[1] - decor[1]).toFloat())
        }
        val start = android.os.SystemClock.uptimeMillis()
        fun send(action: Int, position: Offset) {
            compose.runOnUiThread {
                val event = MotionEvent.obtain(start, android.os.SystemClock.uptimeMillis(), action, 1,
                    arrayOf(MotionEvent.PointerProperties().apply { id = 0; toolType = MotionEvent.TOOL_TYPE_FINGER }),
                    arrayOf(MotionEvent.PointerCoords().apply { x = position.x; y = position.y; pressure = 1f; size = 1f }),
                    0, 0, 1f, 1f, 0, 0, InputDevice.SOURCE_TOUCHSCREEN, 0)
                try { compose.activity.dispatchTouchEvent(event) } finally { event.recycle() }
            }
            compose.mainClock.advanceTimeByFrame()
            compose.waitForIdle()
        }
        send(MotionEvent.ACTION_DOWN, point)
        val end = if (drag) point + Offset(0f, 80f) else point
        if (drag) send(MotionEvent.ACTION_MOVE, end)
        send(if (cancel) MotionEvent.ACTION_CANCEL else MotionEvent.ACTION_UP, end)
        compose.waitForIdle()
    }

    @Test fun fontResetHasOnePulseForTouchAndForAccessibility() {
        var custom by mutableStateOf(true)
        var resets = 0
        compose.setContent { Host {
            FontWeightSetting(custom, 100, "Hinge", onReset = { custom = false; resets++ }, onCommit = {})
        } }
        pulses.clear()
        touch(compose.onNodeWithText("重置"))
        assertEquals(1, resets)
        assertEquals("Activity and component must share one tap", 1, pulses.size)
        compose.runOnIdle { custom = true }; pulses.clear()
        compose.onNodeWithText("重置").performClick()
        assertEquals(2, resets)
        assertEquals("Accessibility does not pass through Activity touch", 1, pulses.size)
    }

    @Test fun glassEntryDialogSelectionAndCancelEachHaveOnePulse() {
        var clear by mutableStateOf(false)
        var selections = 0
        compose.setContent { Host {
            NavigationGlassChoice(clear) { clear = it; selections++ }
        } }
        pulses.clear()
        touch(compose.onNodeWithTag("glass-choice-entry"))
        assertEquals(1, pulses.size)
        pulses.clear()
        compose.onNodeWithTag("glass-choice-row-true").performTouchInput { click() }
        assertTrue(clear); assertEquals(1, selections); assertEquals(1, pulses.size)
        pulses.clear()
        touch(compose.onNodeWithTag("glass-choice-entry"))
        assertEquals(1, pulses.size)
        pulses.clear()
        compose.onNodeWithText("取消").performTouchInput { click() }
        assertEquals(1, pulses.size)
    }

    @Test fun builtInControlsOrdinaryTapsAndRapidRepeatedTapsKeepOnePulse() {
        var checked by mutableStateOf(false)
        var value by mutableFloatStateOf(-300f)
        var clicks = 0
        compose.setContent { Host { Column {
            Switch(checked, { checked = it }, Modifier.testTag("switch"))
            Slider(value, { value = it }, Modifier.testTag("slider"), steps = 5, valueRange = -300f..300f)
            Box(Modifier.fillMaxWidth().height(48.dp).testTag("ordinary").clickable { clicks++ })
        } } }
        pulses.clear(); touch(compose.onNodeWithTag("switch"))
        assertTrue(checked); assertEquals(1, pulses.size)
        pulses.clear(); touch(compose.onNodeWithTag("slider"))
        assertEquals(1, pulses.size)
        pulses.clear(); repeat(2) { touch(compose.onNodeWithTag("ordinary")) }
        assertEquals(2, clicks); assertEquals(2, pulses.size)
        pulses.clear(); touch(compose.onNodeWithTag("ordinary"), drag = true)
        touch(compose.onNodeWithTag("ordinary"), cancel = true)
        assertEquals(2, clicks); assertTrue(pulses.isEmpty())
    }

    @Test fun disabledFeedbackKeepsActionsAndDialogsWorkingWithoutPulses() {
        compose.activity.feedbackEnabled = false
        var custom by mutableStateOf(true)
        compose.setContent { Host { Column {
            FontWeightSetting(custom, 100, "Hinge", hapticsEnabled = false, onReset = { custom = false }, onCommit = {})
            NavigationGlassChoice(false, hapticsEnabled = false) {}
        } } }
        pulses.clear(); touch(compose.onNodeWithText("重置")); assertFalse(custom)
        touch(compose.onNodeWithTag("glass-choice-entry"))
        compose.onNodeWithText("取消").performTouchInput { click() }
        assertTrue(pulses.isEmpty())
    }

    @Test fun clipboardSwitchAndPairingActionsHaveOnePulseEach() {
        var enabled by mutableStateOf(true)
        var changes = 0
        val choices = mutableListOf<Boolean>()
        var pairing by mutableStateOf(false)
        compose.setContent { Host {
            if (pairing) WirelessAdbContent(true, "", { choices += it })
            else ClipboardSyncContent(enabled, false, { enabled = it; changes++ }, { pairing = true })
        } }
        pulses.clear(); compose.onNodeWithText("剪贴板同步").performScrollTo(); touch(compose.onNodeWithText("剪贴板同步"))
        assertFalse(enabled); assertEquals(1, changes); assertEquals(1, pulses.size)
        pulses.clear(); compose.onNodeWithText("剪贴板同步").performClick()
        assertTrue(enabled); assertEquals(2, changes); assertEquals(1, pulses.size)
        pulses.clear(); touch(compose.onNodeWithText("剪贴板同步"), rightEdge = true)
        assertFalse(enabled); assertEquals(3, changes); assertEquals(1, pulses.size)
        compose.onNodeWithText("连接无线 ADB，并开启电脑端剪贴板同步后生效。").assertDoesNotExist()
        compose.onNodeWithText("无线 ADB").assertDoesNotExist()
        compose.onNodeWithText("剪贴板同步").performClick()
        compose.onNodeWithText("连接无线 ADB，并开启电脑端剪贴板同步后生效。").assertExists()
        pulses.clear(); touch(compose.onNodeWithText("无线 ADB"))
        assertEquals(1, pulses.size)
        pulses.clear(); compose.onNodeWithText("扫码配对").performScrollTo(); touch(compose.onNodeWithText("扫码配对"))
        assertEquals(listOf(true), choices); assertEquals(1, pulses.size)
        pulses.clear(); compose.onNodeWithText("配对码配对").performScrollTo(); touch(compose.onNodeWithText("配对码配对"))
        assertEquals(listOf(true, false), choices); assertEquals(1, pulses.size)
        compose.activity.feedbackEnabled = false
        pulses.clear(); compose.onNodeWithText("扫码配对").performScrollTo(); touch(compose.onNodeWithText("扫码配对"))
        assertEquals(3, choices.size); assertTrue(pulses.isEmpty())
    }

    @Test @Config(sdk = [35], qualifiers = "zh-rCN-w320dp-h800dp-mdpi")
    fun clipboardLayoutSupportsLargeTextAndBothThemes() = renderClipboard(1.5f)

    @Test @Config(sdk = [35])
    fun clipboardDefaultLayout() = renderClipboard(1f)

    private fun renderClipboard(scale: Float) {
        var dark by mutableStateOf(false)
        compose.setContent { Host {
            val density = androidx.compose.ui.platform.LocalDensity.current
            CompositionLocalProvider(androidx.compose.ui.platform.LocalDensity provides
                androidx.compose.ui.unit.Density(density.density, scale)) {
                MaterialTheme(colorScheme = if (dark) darkColorScheme() else lightColorScheme()) {
                    Surface(Modifier.fillMaxSize()) {
                        WirelessAdbContent(true, "", {})
                    }
                }
            }
        } }
        for (night in listOf(false, true)) {
            compose.runOnIdle { dark = night }
            compose.onNodeWithText("扫码配对").performScrollTo().assertIsDisplayed()
            compose.onNodeWithText("配对码配对").performScrollTo().assertIsDisplayed()
            compose.runOnUiThread {
                val root = android.view.inspector.WindowInspector.getGlobalWindowViews().last { it.width > 0 && it.height > 0 }
                val bitmap = android.graphics.Bitmap.createBitmap(root.width, root.height, android.graphics.Bitmap.Config.ARGB_8888)
                root.draw(android.graphics.Canvas(bitmap))
                java.io.File("build/parity/clipboard-$scale-$night.png").apply { parentFile?.mkdirs() }.outputStream().use {
                    bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it)
                }
                bitmap.recycle()
            }
        }
    }
}
