package com.hinge.office

import android.os.Build
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.clickable as foundationClickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.material3.Text as GlassChoiceText
import androidx.compose.material3.TextButton as GlassChoiceTextButton
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.graphics.vector.PathParser
import androidx.compose.ui.platform.*
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.PlatformTextStyle
import androidx.compose.ui.text.font.*
import androidx.compose.ui.unit.*
import androidx.compose.ui.window.DialogWindowProvider
import com.hinge.office.R

// One reference implementation in all ten apps; host settings supply only scale, color and persistence.
@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun NavigationGlassChoice(
    translucent: Boolean,
    fontScale: Float = LocalDensity.current.fontScale,
    hapticsEnabled: Boolean = true,
    onSelect: (Boolean) -> Unit,
) {
    var open by remember { mutableStateOf(false) }
    val view = LocalView.current
    val clickFeedback = LocalHingeSettingsClickFeedback.current
    val density = LocalDensity.current
    val context = LocalContext.current
    val configuration = LocalConfiguration.current
    val weightDelta = (MaterialTheme.typography.bodyMedium.fontWeight?.weight ?: 400) - 400
    fun style(size: Float, height: Float, weight: Int) = TextStyle(
        fontFamily = FontFamily.Default, fontSize = (size * fontScale).sp,
        lineHeight = (height * fontScale).sp, letterSpacing = 0.sp,
        fontWeight = FontWeight((weight + weightDelta).coerceIn(100, 900)),
        platformStyle = PlatformTextStyle(includeFontPadding = false),
    )
    val typography = MaterialTheme.typography.copy(
        headlineSmall = style(24f, 31.92f, 400),
        titleMedium = style(16f, 24f, 600),
        bodyMedium = style(14f, 20.02f, 400),
        labelLarge = style(14f, 20.02f, 600),
    )
    val fontContext = remember(context, configuration) {
        if (Build.VERSION.SDK_INT >= 31) context.createConfigurationContext(
            android.content.res.Configuration(configuration).apply { fontWeightAdjustment = 0 }
        ) else context
    }
    val resolver = remember(fontContext) { createFontFamilyResolver(fontContext) }
    fun feedback() {
        if (hapticsEnabled) clickFeedback(view)
    }
    MaterialTheme(typography = typography) {
        CompositionLocalProvider(
            LocalDensity provides Density(density.density, 1f),
            LocalFontFamilyResolver provides resolver,
            LocalRippleConfiguration provides null,
            LocalMinimumInteractiveComponentSize provides 48.dp,
        ) {
            Row(Modifier.testTag("glass-choice-entry").fillMaxWidth().heightIn(min = 64.dp)
                .foundationClickable(interactionSource = remember { MutableInteractionSource() }, indication = null) {
                    feedback(); open = true
                }.padding(horizontal = 14.dp, vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                GlassChoiceIcon(GLASS_CHOICE_BLUR, MaterialTheme.colorScheme.onSurfaceVariant)
                Spacer(Modifier.width(16.dp))
                Column(Modifier.weight(1f)) {
                    GlassChoiceText("底栏玻璃样式", style = typography.titleMedium, color = MaterialTheme.colorScheme.onSurface)
                    GlassChoiceText(if (translucent) "经典玻璃" else "通透磨砂", style = typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Spacer(Modifier.width(16.dp))
                GlassChoiceIcon(GLASS_CHOICE_CHEVRON, MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if (open) AlertDialog(
                onDismissRequest = { open = false },
                shape = RoundedCornerShape(28.dp),
                title = {
                    val window = (LocalView.current.parent as? DialogWindowProvider)?.window
                    SideEffect {
                        window?.setWindowAnimations(R.style.UnifiedGlassChoiceAnimation)
                        window?.setDimAmount(.5f)
                    }
                    GlassChoiceText("底栏玻璃样式", style = typography.headlineSmall)
                },
                text = {
                    Column(Modifier.heightIn(max = 360.dp).verticalScroll(rememberScrollState()).selectableGroup()) {
                        listOf(false, true).forEach { clear ->
                            Row(Modifier.testTag("glass-choice-row-$clear").fillMaxWidth().heightIn(min = 72.dp)
                                .selectable(selected = translucent == clear, role = Role.RadioButton,
                                    interactionSource = remember { MutableInteractionSource() }, indication = null,
                                    onClick = { feedback(); onSelect(clear); open = false })
                                .padding(vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                                    GlassChoiceText(if (clear) "经典玻璃" else "通透磨砂", style = typography.titleMedium,
                                        color = MaterialTheme.colorScheme.onSurface)
                                    GlassChoiceText(if (clear) "更多背景透出，对比度更低" else "底色更厚实，可读性更高",
                                        style = typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                                }
                                Spacer(Modifier.width(16.dp))
                                RadioButton(translucent == clear, null, modifier = Modifier.testTag("glass-choice-radio-$clear"))
                            }
                        }
                    }
                },
                confirmButton = { GlassChoiceTextButton(onClick = { feedback(); open = false }) { GlassChoiceText("取消") } },
            )
        }
    }
}

@Composable
private fun GlassChoiceIcon(data: String, color: Color) {
    val path = remember(data) { PathParser().parsePathString(data).toPath() }
    Canvas(Modifier.size(24.dp)) { scale(size.width / 24f, size.height / 24f, pivot = androidx.compose.ui.geometry.Offset.Zero) { drawPath(path, color) } }
}

// Material Symbols rounded, Apache-2.0; paths retained from Google's material-design-icons.
private const val GLASS_CHOICE_BLUR = "M6 13c-.55 0-1 .45-1 1s.45 1 1 1 1-.45 1-1-.45-1-1-1zm0 4c-.55 0-1 .45-1 1s.45 1 1 1 1-.45 1-1-.45-1-1-1zm0-8c-.55 0-1 .45-1 1s.45 1 1 1 1-.45 1-1-.45-1-1-1zm-3 .5c-.28 0-.5.22-.5.5s.22.5.5.5.5-.22.5-.5-.22-.5-.5-.5zM6 5c-.55 0-1 .45-1 1s.45 1 1 1 1-.45 1-1-.45-1-1-1zm15 5.5c.28 0 .5-.22.5-.5s-.22-.5-.5-.5-.5.22-.5.5.22.5.5.5zM14 7c.55 0 1-.45 1-1s-.45-1-1-1-1 .45-1 1 .45 1 1 1zm0-3.5c.28 0 .5-.22.5-.5s-.22-.5-.5-.5-.5.22-.5.5.22.5.5.5zm-11 10c-.28 0-.5.22-.5.5s.22.5.5.5.5-.22.5-.5-.22-.5-.5-.5zm7 7c-.28 0-.5.22-.5.5s.22.5.5.5.5-.22.5-.5-.22-.5-.5-.5zm0-17c.28 0 .5-.22.5-.5s-.22-.5-.5-.5-.5.22-.5.5.22.5.5.5zM10 7c.55 0 1-.45 1-1s-.45-1-1-1-1 .45-1 1 .45 1 1 1zm0 5.5c-.83 0-1.5.67-1.5 1.5s.67 1.5 1.5 1.5 1.5-.67 1.5-1.5-.67-1.5-1.5-1.5zm8 .5c-.55 0-1 .45-1 1s.45 1 1 1 1-.45 1-1-.45-1-1-1zm0 4c-.55 0-1 .45-1 1s.45 1 1 1 1-.45 1-1-.45-1-1-1zm0-8c-.55 0-1 .45-1 1s.45 1 1 1 1-.45 1-1-.45-1-1-1zm0-4c-.55 0-1 .45-1 1s.45 1 1 1 1-.45 1-1-.45-1-1-1zm3 8.5c-.28 0-.5.22-.5.5s.22.5.5.5.5-.22.5-.5-.22-.5-.5-.5zM14 17c-.55 0-1 .45-1 1s.45 1 1 1 1-.45 1-1-.45-1-1-1zm0 3.5c-.28 0-.5.22-.5.5s.22.5.5.5.5-.22.5-.5-.22-.5-.5-.5zm-4-12c-.83 0-1.5.67-1.5 1.5s.67 1.5 1.5 1.5 1.5-.67 1.5-1.5-.67-1.5-1.5-1.5zm0 8.5c-.55 0-1 .45-1 1s.45 1 1 1 1-.45 1-1-.45-1-1-1zm4-4.5c-.83 0-1.5.67-1.5 1.5s.67 1.5 1.5 1.5 1.5-.67 1.5-1.5-.67-1.5-1.5-1.5zm0-4c-.83 0-1.5.67-1.5 1.5s.67 1.5 1.5 1.5 1.5-.67 1.5-1.5-.67-1.5-1.5-1.5z"
private const val GLASS_CHOICE_CHEVRON = "M9.29 6.71c-.39.39-.39 1.02 0 1.41L13.17 12l-3.88 3.88c-.39.39-.39 1.02 0 1.41.39.39 1.02.39 1.41 0l4.59-4.59c.39-.39.39-1.02 0-1.41L10.7 6.7c-.38-.38-1.02-.38-1.41.01z"
