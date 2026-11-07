@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)

package com.hinge.office

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.EnterTransition
import androidx.compose.animation.ExitTransition
import androidx.compose.animation.core.FastOutLinearInEasing
import androidx.compose.animation.core.LinearOutSlowInEasing
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.AbsoluteAlignment
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp

@Composable
internal fun DesktopToolsPanel(
    visible: Boolean,
    active: Boolean,
    reducedMotion: Boolean,
    dismiss: () -> Unit,
    content: @Composable (compact: Boolean, panelWidth: Dp) -> Unit,
) {
    BackHandler(enabled = active && visible, onBack = dismiss)
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val compact = maxWidth > maxHeight
        val sidePanel = compact || maxWidth >= 600.dp
        val width = when {
            compact -> (maxWidth * .4f).coerceIn(300.dp, 400.dp)
            sidePanel -> (maxWidth * .45f).coerceIn(360.dp, 440.dp)
            else -> maxWidth
        }
        val ime = WindowInsets.ime.asPaddingValues().calculateBottomPadding()
        val height = ((if (sidePanel) maxHeight else maxHeight * .92f) - ime)
            .coerceAtLeast(120.dp).coerceAtMost(maxHeight)

        AnimatedVisibility(
            visible = visible,
            modifier = Modifier.matchParentSize(),
            enter = if (reducedMotion) EnterTransition.None else fadeIn(tween(120)),
            exit = if (reducedMotion) ExitTransition.None else fadeOut(tween(120)),
        ) {
            Box(Modifier.fillMaxSize()
                .background(Color.Black.copy(alpha = .38f))
                .clickable(onClick = dismiss)
                .semantics { contentDescription = "关闭桌面工具" })
        }
        Box(
            Modifier.matchParentSize().windowInsetsPadding(WindowInsets.safeDrawing).clipToBounds(),
            contentAlignment = if (sidePanel) AbsoluteAlignment.CenterRight else Alignment.BottomCenter,
        ) {
            val enter = tween<IntOffset>(140, easing = LinearOutSlowInEasing)
            val exit = tween<IntOffset>(120, easing = FastOutLinearInEasing)
            AnimatedVisibility(
                visible = visible,
                enter = when {
                    reducedMotion -> EnterTransition.None
                    sidePanel -> slideInHorizontally(enter) { it }
                    else -> slideInVertically(enter) { it }
                },
                exit = when {
                    reducedMotion -> ExitTransition.None
                    sidePanel -> slideOutHorizontally(exit) { it }
                    else -> slideOutVertically(exit) { it }
                },
            ) {
                Surface(
                    Modifier.width(width).height(height),
                    shape = RoundedCornerShape(if (compact) 16.dp else 24.dp),
                    color = MaterialTheme.colorScheme.surfaceContainer,
                ) { content(compact, width) }
            }
        }
    }
}

@Composable
internal fun DesktopInputModeButtons(
    direct: Boolean,
    gameCamera: Boolean,
    enabled: Boolean,
    absoluteSupported: Boolean,
    inset: Dp,
    select: (Boolean) -> Unit,
) {
    SingleChoiceSegmentedButtonRow(Modifier.fillMaxWidth().padding(inset), space = 0.dp) {
        listOf("屏幕触控", "鼠标指针").forEachIndexed { index, label ->
            val touch = index == 0
            SegmentedButton(
                selected = !gameCamera && touch == (direct && absoluteSupported),
                onClick = { select(touch) },
                enabled = enabled && (!touch || absoluteSupported),
                shape = SegmentedButtonDefaults.itemShape(index, 2),
                border = BorderStroke(0.dp, Color.Transparent),
                icon = {},
            ) {
                Text(label, maxLines = 1, overflow = TextOverflow.Ellipsis,
                    style = MaterialTheme.typography.labelLarge)
            }
        }
    }
}
