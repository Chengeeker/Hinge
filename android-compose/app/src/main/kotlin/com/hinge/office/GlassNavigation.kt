package com.hinge.office

import android.graphics.Bitmap
import android.os.Build
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.draw.drawWithCache
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.input.InputMode
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.graphics.drawscope.translate
import androidx.compose.ui.graphics.layer.GraphicsLayer
import androidx.compose.ui.graphics.layer.drawLayer
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.graphics.rememberGraphicsLayer
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.layout.positionInRoot
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.platform.LocalInputModeManager
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.unit.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.first
import java.util.concurrent.Executors
import kotlin.math.roundToInt

class GlassBackdrop(val layer: GraphicsLayer) {
    var version = 0L
    var origin = Offset.Zero
}

@Composable
fun rememberGlassBackdrop(): GlassBackdrop {
    val layer = rememberGraphicsLayer()
    return remember(layer) { GlassBackdrop(layer) }
}

val LocalGlassBackdrop = staticCompositionLocalOf<GlassBackdrop?> { null }

fun Modifier.glassSource(backdrop: GlassBackdrop) =
    onGloballyPositioned { backdrop.origin = it.positionInRoot() }.drawWithContent {
        backdrop.layer.record { this@drawWithContent.drawContent() }
        drawLayer(backdrop.layer)
        backdrop.version++
    }

private val glassTabs = listOf("首页" to "home", "工作区" to "workspaces", "设置" to "settings")

private data class PageCapture(val version: Long, val offset: Offset)
private data class GlassFrame(val motion: GlassMotion, val page: PageCapture, val art: Pair<Long, Long>)

@Composable
fun GlassNavigation(model: HingeModel, width: Dp, selected: Int, tabBacking: State<Float>, select: (Int) -> Unit, visible: Boolean) {
    val backdrop = LocalGlassBackdrop.current ?: return
    val crop = rememberGraphicsLayer()
    val inactive = rememberGlassBackdrop()
    val active = rememberGlassBackdrop()
    val density = LocalDensity.current
    val layout = LocalLayoutDirection.current
    val view = LocalView.current
    val inputMode = LocalInputModeManager.current
    var focused by remember { mutableStateOf<Int?>(null) }
    val reducedMotion = rememberGlassReducedMotion(model.activity)
    val reduceMotion by rememberUpdatedState(reducedMotion)
    val rtl = layout == LayoutDirection.Rtl
    val selectedSlot by rememberUpdatedState(if (rtl) 2 - selected else selected)
    val selectTab by rememberUpdatedState(select)
    val drawing by rememberUpdatedState(visible)
    val surface = MaterialTheme.colorScheme.surface
    val dark = surface.luminance() < .5f
    val inactiveColor = if (dark) Color.White else MaterialTheme.colorScheme.onSurfaceVariant
    val activeColor = MaterialTheme.colorScheme.primary
    var origin by remember { mutableStateOf(Offset.Zero) }
    var longPressed by remember { mutableStateOf(false) }
    var dragTarget by remember { mutableStateOf<Float?>(null) }
    val pressure = animateFloatAsState(
        targetValue = if (longPressed) 1f else 0f,
        animationSpec = glassPressureAnimation(longPressed, reducedMotion),
        label = "glass-pressure",
    )
    val liveMotion = remember { mutableStateOf(GlassMotion(selected.toFloat(), 0f, 0f)) }
    LaunchedEffect(backdrop, width, density, layout) {
        val spring = GlassSpring(selectedSlot.toFloat())
        var settlingDrag = false
        var previousTime = 0L
        while (isActive) {
            if (!drawing) { snapshotFlow { drawing }.first { it }; previousTime = 0L }
            val time = withFrameNanos { it }
            if (!drawing) continue
            val seconds = if (previousTime == 0L) 0f else ((time - previousTime) / 1e9).toFloat()
            previousTime = time
            val drag = dragTarget
            val target = selectedSlot.toFloat()
            val backing = tabBacking.value.let { if (rtl) 2f - it else it }
            if (drag != null) settlingDrag = true
            if (settlingDrag && !reduceMotion) {
                spring.step(drag ?: target, seconds, if (drag != null) .15f else .35f, if (drag != null) .86f else .85f)
                if (drag == null && spring.value == target && backing == target) settlingDrag = false
            } else { spring.snap(drag ?: backing); if (drag == null) settlingDrag = false }
            liveMotion.value = GlassMotion(spring.value, spring.velocity, pressure.value)
        }
    }

    if (!visible) return
    Box(Modifier.width(width).height(GLASS_HEIGHT.dp).onGloballyPositioned {
        origin = it.positionInRoot()
    }) {
        val keyboardFocus = focused.takeIf { inputMode.inputMode == InputMode.Keyboard }
        val normal = rememberGraphicsLayer()
        val selectedArt = rememberGraphicsLayer()
        val drawingArea = Modifier.requiredSize(width + 40.dp, (GLASS_HEIGHT + 30f).dp).align(Alignment.Center)
        Box(drawingArea) {
            Box(Modifier.fillMaxSize().drawWithContent { normal.record { this@drawWithContent.drawContent() } }, contentAlignment = Alignment.Center) {
                Box(Modifier.size(width, GLASS_HEIGHT.dp)) { GlassTabArt(inactive, inactiveColor, false, true, keyboardFocus, dark) }
            }
            Box(Modifier.fillMaxSize().drawWithContent { selectedArt.record { this@drawWithContent.drawContent() } }, contentAlignment = Alignment.Center) {
                Box(Modifier.size(width, GLASS_HEIGHT.dp)) { GlassTabArt(active, activeColor, true, true, null, dark) }
            }
            val motion = liveMotion.value
            NavigationGlassMaterial(backdrop.layer, { backdrop.origin }, { backdrop.version }, normal, selectedArt,
                motion.position, motion.pressure, dark, surface, model.settings["translucentNavigationGlass"] as? Boolean ?: false,
                glassTabs.size, Modifier.fillMaxSize())
        }
        Row(Modifier.fillMaxSize().selectableGroup().glassTabGestures(width, layout, { selectedSlot },
            onDrag = { dragTarget = it }, onLongPress = { longPressed = it },
            onGestureStarted = model.activity::onGlassGestureStarted,
            selectSlot = { selectTab(if (rtl) 2 - it else it) },
        )) {
            glassTabs.forEachIndexed { index, (label, _) ->
                Box(Modifier.weight(1f).fillMaxHeight()
                    .onFocusChanged { if (it.isFocused) focused = index else if (focused == index) focused = null }
                    .selectable(selected = selected == index, role = Role.Tab, onClick = { select(index) })
                    .semantics { contentDescription = label })
            }
        }
    }
}

@Composable
private fun GlassTabArt(layer: GlassBackdrop, color: Color, selectedStyle: Boolean, visible: Boolean,
    focused: Int?, dark: Boolean) {
    Row(Modifier.fillMaxSize().clearAndSetSemantics {}.drawWithContent {
        layer.layer.record { this@drawWithContent.drawContent() }
        layer.version++
        if (visible) drawLayer(layer.layer)
    }.padding(horizontal = 4.dp)) {
        glassTabs.forEachIndexed { index, (label, icon) ->
            Layout(modifier = Modifier.weight(1f).fillMaxHeight().then(
                if (focused == index) Modifier.glassFocusRing(dark) else Modifier), content = {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    val density = LocalDensity.current
                    CompositionLocalProvider(LocalDensity provides Density(density.density, fontScale = 1f)) {
                        val code = OriginalIcons.load(androidx.compose.ui.platform.LocalContext.current)
                            .getInt("Symbols.${icon}_rounded")
                        Text(String(Character.toChars(code)), Modifier.size(24.dp), color = color,
                            style = TextStyle(fontFamily = FontFamily(Font(if (selectedStyle)
                                R.font.glass_symbols_filled else R.font.glass_symbols_outline)),
                                fontSize = 24.sp, lineHeight = 24.sp))
                    }
                    Spacer(Modifier.height(4.dp))
                    Text(label, color = color, fontSize = 11.sp, lineHeight = (11f * 1.43f).sp,
                        letterSpacing = 0.sp,
                        fontWeight = if (selectedStyle) FontWeight.W600 else FontWeight.W500, maxLines = 1)
                }
            }) { measurables, constraints ->
                val item = measurables.single().measure(Constraints())
                val fitted = minOf(1f, constraints.maxWidth.toFloat() / item.width,
                    constraints.maxHeight.toFloat() / item.height)
                layout(constraints.maxWidth, constraints.maxHeight) {
                    item.placeWithLayer((constraints.maxWidth - item.width) / 2,
                        (constraints.maxHeight - item.height) / 2) {
                        scaleX = fitted
                        scaleY = fitted
                    }
                }
            }
        }
    }
}

private fun Modifier.glassFocusRing(dark: Boolean) = drawWithCache {
    val inset = 22.dp.toPx()
    val bitmap = Bitmap.createBitmap((size.width + 2f * inset).roundToInt(),
        (size.height + 2f * inset).roundToInt(), Bitmap.Config.ARGB_8888)
    val canvas = android.graphics.Canvas(bitmap)
    val rect = android.graphics.RectF(inset - 3.dp.toPx(), inset - 3.dp.toPx(),
        inset + size.width + 3.dp.toPx(), inset + size.height + 3.dp.toPx())
    val paint = android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG).apply {
        color = if (dark) 0xff0a84ff.toInt() else 0xff007aff.toInt()
        style = android.graphics.Paint.Style.STROKE
    }
    for ((width, alpha) in listOf(14f to .12f, 8f to .25f, 1.5f to .85f)) {
        paint.strokeWidth = width.dp.toPx()
        paint.alpha = (255f * alpha).roundToInt()
        paint.maskFilter = if (width == 14f) android.graphics.BlurMaskFilter(4.dp.toPx(),
            android.graphics.BlurMaskFilter.Blur.NORMAL) else null
        canvas.drawRoundRect(rect, 12.dp.toPx(), 12.dp.toPx(), paint)
    }
    val ring = bitmap.asImageBitmap()
    onDrawWithContent {
        drawContent()
        drawImage(ring, Offset(-inset, -inset))
    }
}

@Composable
internal fun rememberGlassReducedMotion(context: android.content.Context): Boolean {
    val resolver = context.contentResolver
    val key = android.provider.Settings.Global.TRANSITION_ANIMATION_SCALE
    fun read() = android.provider.Settings.Global.getFloat(resolver, key, 1f) == 0f
    var reduced by remember(resolver) { mutableStateOf(read()) }
    DisposableEffect(resolver) {
        val observer = object : android.database.ContentObserver(android.os.Handler(android.os.Looper.getMainLooper())) {
            override fun onChange(selfChange: Boolean) { reduced = read() }
        }
        resolver.registerContentObserver(android.provider.Settings.Global.getUriFor(key), false, observer)
        onDispose { resolver.unregisterContentObserver(observer) }
    }
    return reduced
}

