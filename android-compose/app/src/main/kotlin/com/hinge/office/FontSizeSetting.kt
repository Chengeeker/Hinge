package com.hinge.office

import android.os.Build
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.material3.Text as SizeText
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.platform.*
import androidx.compose.ui.semantics.*
import androidx.compose.ui.text.*
import androidx.compose.ui.text.font.*
import androidx.compose.ui.unit.*
import kotlin.math.roundToInt

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun FontSizeSetting(value: Float, enabled: Boolean = true,
    fontScale: Float = LocalDensity.current.fontScale, onCommit: (Float) -> Unit) {
    var draft by remember(value) { mutableFloatStateOf(value.takeIf { it.isFinite() } ?: 1f) }
    val colors = MaterialTheme.colorScheme
    val delta = (MaterialTheme.typography.bodyMedium.fontWeight?.weight ?: 400) - 400
    fun style(size: Float, height: Float) = TextStyle(fontFamily = FontFamily.Default,
        fontSize = (size * fontScale).sp, lineHeight = (height * fontScale).sp, letterSpacing = 0.sp,
        fontWeight = FontWeight((600 + delta).coerceIn(100, 900)), platformStyle = PlatformTextStyle(includeFontPadding = false))
    val density = LocalDensity.current
    val context = LocalContext.current
    val configuration = LocalConfiguration.current
    val fontContext = remember(context, configuration) {
        if (Build.VERSION.SDK_INT >= 31) context.createConfigurationContext(
            android.content.res.Configuration(configuration).apply { fontWeightAdjustment = 0 }) else context
    }
    val resolver = remember(fontContext) { createFontFamilyResolver(fontContext) }
    CompositionLocalProvider(LocalDensity provides Density(density.density, 1f),
        LocalFontFamilyResolver provides resolver, LocalRippleConfiguration provides null,
        LocalMinimumInteractiveComponentSize provides 48.dp) {
        Column(Modifier.fillMaxWidth()) {
            SizeText("字体大小", Modifier.padding(horizontal = 16.dp), style = style(15f, 21f), color = colors.primary)
            Spacer(Modifier.height(12.dp))
            Surface(Modifier.fillMaxWidth().testTag("settings-card:字体大小"), shape = RoundedCornerShape(24.dp),
                color = colors.settingsCardColor(colors.surface)) {
                Row(Modifier.fillMaxWidth().padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                    val path = remember { PathParser().parsePathString(SIZE_PATH).toPath() }
                    Canvas(Modifier.size(24.dp)) { scale(size.width / 24f, size.height / 24f, pivot = androidx.compose.ui.geometry.Offset.Zero) { drawPath(path, colors.primary) } }
                    Spacer(Modifier.width(16.dp))
                    Column(Modifier.weight(1f)) {
                        SizeText("${(draft * 100).roundToInt()}%", Modifier.testTag("font-size-label"), style = style(16f, 24f), color = colors.onSurface)
                        Slider(value = draft.coerceIn(.85f, 1.5f), onValueChange = { draft = it },
                            modifier = Modifier.semantics { contentDescription = "字体大小" }, enabled = enabled,
                            valueRange = .85f..1.5f, steps = 12,
                            onValueChangeFinished = { onCommit((draft * 20).roundToInt().coerceIn(17, 30) / 20f) })
                    }
                }
            }
        }
    }
}

// Google Material rounded format_size, Apache-2.0; license already bundled.
private const val SIZE_PATH = "M9 5.5c0 .83.67 1.5 1.5 1.5H14v10.5c0 .83.67 1.5 1.5 1.5s1.5-.67 1.5-1.5V7h3.5c.83 0 1.5-.67 1.5-1.5S21.33 4 20.5 4h-10C9.67 4 9 4.67 9 5.5zM4.5 12H6v5.5c0 .83.67 1.5 1.5 1.5S9 18.33 9 17.5V12h1.5c.83 0 1.5-.67 1.5-1.5S11.33 9 10.5 9h-6C3.67 9 3 9.67 3 10.5S3.67 12 4.5 12z"
