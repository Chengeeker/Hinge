package com.hinge.office

import android.os.Build
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.material3.Text as WeightText
import androidx.compose.material3.TextButton as WeightTextButton
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.platform.*
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.PlatformTextStyle
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.*
import androidx.compose.ui.unit.*
import kotlin.math.roundToInt

// Kept identical across the apps; only branding and existing preference writes vary.
@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun FontWeightSetting(
    custom: Boolean,
    delta: Int,
    appName: String,
    enabled: Boolean = true,
    fontScale: Float = LocalDensity.current.fontScale,
    systemDelta: Int = systemFontWeightDelta(),
    hapticsEnabled: Boolean = true,
    onReset: () -> Unit,
    onCommit: (Int) -> Unit,
) {
    val committed = (if (custom) delta else systemDelta).coerceIn(-300, 300)
    var draft by remember(custom, committed) { mutableFloatStateOf(committed.toFloat()) }
    val label = if (!custom && draft == committed.toFloat()) "跟随系统" else
        listOf("极细", "细", "偏细", "默认", "中等", "偏粗", "加粗")[((draft + 300) / 100).roundToInt().coerceIn(0, 6)]
    val colors = MaterialTheme.colorScheme
    val appliedDelta = committed
    fun style(size: Float, height: Float, weight: Int) = TextStyle(
        fontFamily = FontFamily.Default, fontSize = (size * fontScale).sp,
        lineHeight = (height * fontScale).sp, letterSpacing = 0.sp,
        fontWeight = FontWeight((weight + appliedDelta).coerceIn(100, 900)),
        platformStyle = PlatformTextStyle(includeFontPadding = false),
    )
    val density = LocalDensity.current
    val context = LocalContext.current
    val configuration = LocalConfiguration.current
    val fontContext = remember(context, configuration) {
        if (Build.VERSION.SDK_INT >= 31) context.createConfigurationContext(
            android.content.res.Configuration(configuration).apply { fontWeightAdjustment = 0 }
        ) else context
    }
    val resolver = remember(fontContext) { createFontFamilyResolver(fontContext) }
    val view = LocalView.current
    val clickFeedback = LocalHingeSettingsClickFeedback.current
    CompositionLocalProvider(
        LocalDensity provides Density(density.density, 1f),
        LocalFontFamilyResolver provides resolver,
        LocalRippleConfiguration provides null,
        LocalMinimumInteractiveComponentSize provides 48.dp,
    ) {
        Column(Modifier.fillMaxWidth()) {
            WeightText("字体粗细", Modifier.padding(horizontal = 16.dp),
                style = style(15f, 21f, 600), color = colors.primary)
            Spacer(Modifier.height(12.dp))
            Surface(Modifier.fillMaxWidth().testTag("settings-card:字体粗细"),
                shape = RoundedCornerShape(24.dp),
                color = colors.settingsCardColor(colors.surface)) {
                Column {
                    Row(Modifier.fillMaxWidth().padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                        val path = remember { PathParser().parsePathString(BOLD_PATH).toPath() }
                        Canvas(Modifier.size(24.dp)) {
                            scale(size.width / 24f, size.height / 24f, pivot = androidx.compose.ui.geometry.Offset.Zero) { drawPath(path, colors.primary) }
                        }
                        Spacer(Modifier.width(16.dp))
                        Column(Modifier.weight(1f)) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                WeightText(label, Modifier.weight(1f).testTag("font-weight-label"),
                                    style = style(16f, 24f, 600), color = colors.onSurface)
                                WeightTextButton(onClick = {
                                    if (hapticsEnabled) clickFeedback(view)
                                    onReset()
                                }, enabled = enabled && custom) {
                                    WeightText("重置", style = style(14f, 20.02f, 600))
                                }
                            }
                            Slider(value = draft, onValueChange = { draft = it }, enabled = enabled,
                                modifier = Modifier.semantics { contentDescription = "字体粗细" },
                                valueRange = -300f..300f, steps = 5,
                                onValueChangeFinished = { onCommit((draft / 100).roundToInt().coerceIn(-3, 3) * 100) })
                        }
                    }
                    Column(Modifier.padding(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 14.dp)
                        .background(colors.surfaceContainerHighest.copy(alpha = .3f), RoundedCornerShape(12.dp)).padding(12.dp)) {
                        WeightText("效果预览 ($label)", style = style(11.5f, 20.02f, 600), color = colors.primary)
                        Spacer(Modifier.height(4.dp))
                        WeightText("$appName 极简现代体验：这是一段用于预览字体粗细的示例文本。@开发者 #话题#",
                            Modifier.testTag("font-weight-preview"),
                            style = style(14f, 19.6f, 400).copy(fontWeight = FontWeight((400 + draft.roundToInt()).coerceIn(100, 900))),
                            color = colors.onSurface)
                    }
                }
            }
        }
    }
}

@Composable
internal fun systemFontWeightDelta(): Int {
    val configuration = LocalConfiguration.current
    return if (Build.VERSION.SDK_INT >= 31) configuration.fontWeightAdjustment.takeUnless { it == Int.MAX_VALUE } ?: 0 else 0
}

// Material rounded format_bold, Apache-2.0 (same icon family as RedNote).
private const val BOLD_PATH = "M15.6 10.79c.97-.67 1.65-1.77 1.65-2.79 0-2.26-1.75-4-4-4H8c-.55 0-1 .45-1 1v12c0 .55.45 1 1 1h5.78c2.07 0 3.96-1.69 3.97-3.77.01-1.53-.85-2.84-2.15-3.44zM10 6.5h3c.83 0 1.5.67 1.5 1.5s-.67 1.5-1.5 1.5h-3v-3zm3.5 9H10v-3h3.5c.83 0 1.5.67 1.5 1.5s-.67 1.5-1.5 1.5z"
