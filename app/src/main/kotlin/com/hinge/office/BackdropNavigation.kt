package com.hinge.office

import androidx.annotation.RequiresApi
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.border
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.*
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.drawscope.*
import androidx.compose.ui.graphics.layer.GraphicsLayer
import androidx.compose.ui.graphics.layer.drawLayer
import androidx.compose.ui.layout.LayoutCoordinates
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.*
import com.kyant.backdrop.Backdrop as KyantBackdrop
import com.kyant.backdrop.backdrops.layerBackdrop
import com.kyant.backdrop.backdrops.rememberCombinedBackdrop
import com.kyant.backdrop.backdrops.rememberLayerBackdrop
import com.kyant.backdrop.drawBackdrop
import com.kyant.backdrop.drawPlainBackdrop
import com.kyant.backdrop.effects.blur
import com.kyant.backdrop.effects.lens
import com.kyant.backdrop.highlight.Highlight
import kotlin.math.roundToInt

// Reuse the page's existing GPU recording; never capture the navigation into its own source.
private class NavigationBackdrop(
    val source: GraphicsLayer,
    val origin: () -> Offset,
    val color: Color,
) : KyantBackdrop {
    override val isCoordinatesDependent = true

    override fun DrawScope.drawBackdrop(
        density: Density,
        coordinates: LayoutCoordinates?,
        layerBlock: (GraphicsLayerScope.() -> Unit)?,
    ) {
        val origin = coordinates?.takeIf { it.isAttached }?.positionInRoot() ?: return
        drawRect(color)
        translate(this@NavigationBackdrop.origin().x - origin.x, this@NavigationBackdrop.origin().y - origin.y) {
            drawLayer(source)
        }
    }
}

@RequiresApi(33)
@Composable
internal fun BackdropNavigation(
    backdrop: GraphicsLayer,
    sourceOrigin: () -> Offset,
    labels: GraphicsLayer,
    activeLabels: GraphicsLayer,
    translucent: Boolean,
    position: Float,
    pressed: Float,
    dark: Boolean,
    sceneColor: Color,
    modifier: Modifier,
    count: Int,
) {
    val origin by rememberUpdatedState(sourceOrigin)
    val source = remember(backdrop, sceneColor) { NavigationBackdrop(backdrop, { origin() }, sceneColor) }
    val panel = rememberLayerBackdrop()
    val surface = rememberLayerBackdrop()
    val panelSource = rememberCombinedBackdrop(source, panel)
    val waterSource = rememberCombinedBackdrop(source, surface)
    val density = LocalDensity.current
    val currentPosition by rememberUpdatedState(position)
    var velocity by remember { mutableFloatStateOf(0f) }
    val water = pressed > .001f
    LaunchedEffect(water) {
        velocity = 0f
        if (water) {
            var previousTime = withFrameNanos { it }
            var previousPosition = currentPosition
            while (true) {
                val time = withFrameNanos { it }
                velocity = (currentPosition - previousPosition) * 1_000_000_000f / (time - previousTime)
                previousTime = time
                previousPosition = currentPosition
            }
        }
    }
    val navigationRim = remember { Brush.verticalGradient(listOf(Color.White.copy(alpha = .45f), Color.White.copy(alpha = .12f))) }
    val tint = (if (dark) Color.Black else if (translucent) Color(0xFFF5F5F5) else Color.White)
        .copy(alpha = if (!translucent) .75f else if (dark) .4f else .42f)
    BoxWithConstraints(modifier) {
        val barWidth = maxWidth - 40.dp
        val barHeight = maxHeight - 30.dp
        val slot = (maxWidth - 48.dp) / count
        val jelly = navigationMaterialJelly(velocity, pressed)
        val width = (slot * (1 + .06f * pressed) + 24.dp * pressed) * jelly[0]
        val height = ((barHeight - 8.dp) * (1 + .06f * pressed) + 16.dp * pressed) * jelly[1]
        val left = 24.dp + slot * (position + .5f) - width / 2
        val top = (maxHeight - height) / 2
        val pill = with(density) { Rect(left.toPx(), top.toPx(), (left + width).toPx(), (top + height).toPx()) }
        val mask = remember(pill) {
            Path().apply { addRoundRect(RoundRect(pill, CornerRadius(pill.height / 2))) }
        }
        Box(Modifier.fillMaxSize().layerBackdrop(surface)) {
            Box(
                Modifier.offset(20.dp, 15.dp).size(barWidth, barHeight)
                    .drawBackdrop(
                        backdrop = source,
                        shape = { RoundedCornerShape(50) },
                        effects = {
                            blur((if (translucent && !dark) 6f else 2f).dp.toPx())
                            lens(24.dp.toPx(), 24.dp.toPx())
                        },
                        highlight = { Highlight.Default.copy(alpha = if (dark) .35f else .65f) },
                        shadow = null,
                        exportedBackdrop = panel,
                        onDrawSurface = { drawRect(tint) },
                    )
                    .then(if (dark) Modifier.border(.75.dp, navigationRim, RoundedCornerShape(50)) else Modifier)
            )
            Box(
                Modifier.offset { IntOffset(with(density) { left.toPx().roundToInt() }, with(density) { top.toPx().roundToInt() }) }
                    .size(width, height)
                    .graphicsLayer { alpha = 1f - pressed }
                    .drawPlainBackdrop(
                        backdrop = panelSource,
                        shape = { RoundedCornerShape(50) },
                        effects = { blur(6.dp.toPx()) },
                        onDrawSurface = {
                            drawRect((if (dark) Color.White else Color.Black).copy(
                                alpha = if (translucent && !dark) .08f else 38f / 255,
                            ))
                        },
                    )
            )
            Box(Modifier.fillMaxSize().drawWithContent {
                clipPath(mask, ClipOp.Difference) { drawLayer(labels) }
                clipPath(mask) { drawLayer(activeLabels) }
            })
        }
        if (water) {
            Box(
                Modifier.offset { IntOffset(with(density) { left.toPx().roundToInt() }, with(density) { top.toPx().roundToInt() }) }
                    .size(width, height)
                    .graphicsLayer { alpha = pressed }
                    .drawBackdrop(
                        backdrop = waterSource,
                        shape = { RoundedCornerShape(50) },
                        effects = {
                            lens(10.dp.toPx() * pressed, 14.dp.toPx() * pressed, chromaticAberration = true)
                        },
                        highlight = { Highlight.Default.copy(alpha = pressed) },
                        shadow = null,
                    )
            )
        }
    }
}
