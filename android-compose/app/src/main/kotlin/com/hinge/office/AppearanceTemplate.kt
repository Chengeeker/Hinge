@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
package com.hinge.office

import android.os.Build
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape

import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

internal val appearanceSeeds = listOf(0xFFFA2F3A, 0xFFFF8200, 0xFF1976D2, 0xFF2E7D32, 0xFF7B1FA2, 0xFFE91E63, 0xFF00897B, 0xFF455A64)
internal val appearanceNames = listOf("经典红", "活力橙", "极光蓝", "翡翠绿", "优雅紫", "樱花粉", "青碧色", "经典黑灰")

@Composable
private fun appearanceAction(action: () -> Unit): () -> Unit = LocalView.current.let { view -> LocalHingeSettingsClickFeedback.current.let { pulse -> { pulse(view); action() } } }

@Composable
private fun AppearanceGlyph(name: String, color: Color, size: Int = 24) { HIcon("Symbols." + name + "_rounded", size = size.dp, color = color) }

@Composable
internal fun AppearanceTitle(title: String) {
    Text(title, Modifier.padding(start = 16.dp, end = 16.dp, bottom = 12.dp),
        style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary)
}

@Composable
internal fun AppearanceCard(content: @Composable ColumnScope.() -> Unit) {
    val colors = MaterialTheme.colorScheme
    Surface(Modifier.fillMaxWidth(), shape = RoundedCornerShape(24.dp),
        color = colors.settingsCardColor(colors.surface)) {
        Column(content = content)
    }
}

@Composable
internal fun AppearanceSection(title: String, content: @Composable ColumnScope.() -> Unit) {
    Column {
        AppearanceTitle(title)
        AppearanceCard(content)
    }
}

@Composable
private fun AppearanceRow(title: String, modifier: Modifier, summary: String = "",
    leading: @Composable () -> Unit, trailing: (@Composable () -> Unit)? = null) {
    val colors = MaterialTheme.colorScheme
    Row(modifier.fillMaxWidth().padding(horizontal = 16.dp)
        .heightIn(min = if (summary.isEmpty()) 56.dp else 72.dp), verticalAlignment = Alignment.CenterVertically) {
        leading()
        Spacer(Modifier.width(16.dp))
        Column(Modifier.weight(1f).padding(vertical = 8.dp)) {
            Text(title, style = MaterialTheme.typography.titleMedium.copy(fontSize = 16.sp, lineHeight = 21.6.sp), color = colors.onSurface)
            if (summary.isNotEmpty()) Text(summary, fontSize = 14.sp, lineHeight = 19.6.sp, color = colors.onSurfaceVariant)
        }
        if (trailing != null) { Spacer(Modifier.width(16.dp)); trailing() }
    }
}

@Composable
internal fun AppearanceToggle(title: String, icon: String, value: Boolean, summary: String = "",
    enabled: Boolean = true, change: (Boolean) -> Unit) {
    val action = appearanceAction { change(!value) }
    AppearanceRow(title, Modifier.toggleable(value, enabled = enabled, role = Role.Switch,
        interactionSource = null, indication = null, onValueChange = { action() }).padding(vertical = 3.dp), summary,
        leading = { AppearanceGlyph(icon, MaterialTheme.colorScheme.primary) },
        trailing = { Switch(value, null, modifier = Modifier.padding(horizontal = 4.dp), enabled = enabled) })
}

@Composable
internal fun AppearanceModes(mode: Int, onMode: (Int) -> Unit, pureBlack: Boolean? = null,
    onPureBlack: (Boolean) -> Unit = {}, enabled: Boolean = true) {
    AppearanceSection("明暗模式") {
        Column(Modifier.selectableGroup()) {
            listOf("跟随系统", "浅色模式", "深色模式").forEachIndexed { index, title ->
                AppearanceRow(title, Modifier.selectable(mode == index, enabled = enabled, role = Role.RadioButton,
                    interactionSource = null, indication = null, onClick = appearanceAction { onMode(index) }).padding(vertical = 2.dp),
                    leading = { RadioButton(mode == index, null, Modifier.size(40.dp), enabled = enabled) })
            }
        }
        if (pureBlack != null) AppearanceToggle("纯黑深色模式", "contrast", pureBlack, enabled = enabled, change = onPureBlack)
    }
}

@Composable
internal fun AppearancePalette(dynamic: Boolean, onDynamic: (Boolean) -> Unit, selected: Int,
    onColor: (Int) -> Unit, seeds: List<Long> = appearanceSeeds, names: List<String> = appearanceNames, enabled: Boolean = true) {
    val colors = MaterialTheme.colorScheme
    val supportsDynamic = Build.VERSION.SDK_INT >= 31
    AppearanceSection("色彩方案") {
        Spacer(Modifier.height(8.dp))
        AppearanceToggle("动态取色", "auto_awesome", dynamic && supportsDynamic, enabled = enabled && supportsDynamic, change = onDynamic)
        if (!dynamic || !supportsDynamic) {
            Text("Material 3 预置主题色", Modifier.padding(start = 16.dp, top = 14.dp, bottom = 10.dp),
                fontSize = 14.sp, color = colors.onSurfaceVariant)
            FlowRow(Modifier.padding(horizontal = 12.dp), horizontalArrangement = Arrangement.spacedBy(12.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp)) {
                names.forEachIndexed { index, title ->
                    val checked = selected == index
                    Box(Modifier.heightIn(min = 48.dp).selectable(checked, enabled = enabled, role = Role.RadioButton,
                        interactionSource = null, indication = null, onClick = appearanceAction { onColor(index) }), contentAlignment = Alignment.Center) {
                        Surface(shape = RoundedCornerShape(8.dp), color = if (checked) colors.secondaryContainer else colors.surface,
                            border = if (checked) null else BorderStroke(1.dp, colors.outlineVariant)) {
                            Row(Modifier.padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
                                Box(Modifier.size(20.dp).background(Color(seeds[index]), CircleShape), contentAlignment = Alignment.Center) {
                                    if (checked) {
                                        Box(Modifier.matchParentSize().background(Color.Black.copy(alpha = .38f), CircleShape))
                                        AppearanceGlyph("check", colors.onSurfaceVariant, 18)
                                    }
                                }
                                Text(title, Modifier.padding(horizontal = 8.dp), style = MaterialTheme.typography.labelLarge, color = colors.onSurfaceVariant)
                            }
                        }
                    }
                }
            }
            Spacer(Modifier.height(8.dp))
        }
        Spacer(Modifier.height(8.dp))
    }
}
