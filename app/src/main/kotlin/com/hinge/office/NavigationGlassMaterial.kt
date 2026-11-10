package com.hinge.office

import android.graphics.Bitmap
import android.os.Build
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.*
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.drawscope.*
import androidx.compose.ui.graphics.layer.*
import androidx.compose.ui.layout.*
import androidx.compose.ui.platform.*
import androidx.compose.ui.unit.*
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import kotlinx.coroutines.*

// Keep the application's navigation gestures outside this rendering-only host.
@Composable
internal fun NavigationGlassMaterial(
    source: GraphicsLayer, sourceOrigin: () -> Offset, revision: () -> Long,
    labels: GraphicsLayer, activeLabels: GraphicsLayer,
    position: Float, pressed: Float, dark: Boolean, sceneColor: Color,
    translucent: Boolean, count: Int, modifier: Modifier, active: Boolean = true,
) {
    val context = LocalContext.current
    val density = LocalDensity.current
    val direction = LocalLayoutDirection.current
    val owner = LocalLifecycleOwner.current
    val typography = androidx.compose.material3.MaterialTheme.typography
    val sourceOffset by rememberUpdatedState(sourceOrigin)
    val sourceRevision by rememberUpdatedState(revision)
    if (Build.VERSION.SDK_INT >= 33) {
        BackdropNavigation(source, { sourceOffset() }, labels, activeLabels, translucent,
            position, pressed, dark, sceneColor, modifier, count)
        return
    }
    val view = remember(context) { QweaNavigationView(context) }
    val crop = rememberGraphicsLayer()
    var origin by remember { mutableStateOf(Offset.Zero) }
    var size by remember { mutableStateOf(IntSize.Zero) }
    var failed by remember { mutableStateOf(false) }
    var visible by remember { mutableStateOf(false) }
    val motion by rememberUpdatedState(position to pressed)
    val drawing by rememberUpdatedState(active)
    DisposableEffect(view) {
        view.failure = { failed = true; android.util.Log.w("NavigationGlass", it) }
        onDispose { view.close() }
    }
    LaunchedEffect(view, source, origin, size, density, direction, dark, sceneColor, translucent, count, failed, typography) {
        var previousFrame = 0L
        var previousPosition = motion.first
        var previousMotion: Pair<Float, Float>? = null
        var previousVelocity = 0f
        var previousRevision = -1L
        var previousOrigin = Offset.Unspecified
        var generation = -1
        var lastRead = 0L
        suspend fun GraphicsLayer.bitmap(): Bitmap {
            val original = toImageBitmap().asAndroidBitmap()
            if (original.config == Bitmap.Config.ARGB_8888) return original
            return try {
                withContext(Dispatchers.Default) { requireNotNull(original.copy(Bitmap.Config.ARGB_8888, false)) }
            } finally { original.recycle() }
        }
        while (isActive && !failed) {
            val frame = withFrameNanos { it }
            val velocity = if (previousFrame == 0L) 0f else
                (motion.first - previousPosition) * 1_000_000_000f / (frame - previousFrame)
            previousFrame = frame
            previousPosition = motion.first
            if (!drawing || !view.ready || size.width == 0 || size.height == 0 ||
                labels.size != size || activeLabels.size != size ||
                !owner.lifecycle.currentState.isAtLeast(Lifecycle.State.STARTED)) continue
            val currentRevision = sourceRevision()
            val offset = sourceOffset()
            val currentGeneration = view.surfaceGeneration
            val read = currentGeneration != generation || offset != previousOrigin ||
                (currentRevision != previousRevision && frame - lastRead >= 32_000_000L)
            if (!read && motion == previousMotion && velocity == previousVelocity) continue
            var background: Bitmap? = null
            var normal: Bitmap? = null
            var selected: Bitmap? = null
            try {
                if (read) {
                    val sampled = IntSize((size.width + 1) / 2, (size.height + 1) / 2)
                    crop.record(density, direction, sampled) {
                        drawRect(sceneColor)
                        scale(sampled.width.toFloat() / size.width, sampled.height.toFloat() / size.height, Offset.Zero) {
                            translate(offset.x - origin.x, offset.y - origin.y) { drawLayer(source) }
                        }
                    }
                    background = crop.bitmap()
                    if (currentGeneration != generation) {
                        normal = labels.bitmap()
                        selected = activeLabels.bitmap()
                    }
                    lastRead = frame
                }
                val done = CompletableDeferred<Boolean>()
                view.render(background, normal, motion.first, motion.second, dark, density.density,
                    lensVelocity = velocity, selectedLabels = selected, generation = currentGeneration,
                    translucent = translucent, count = count) { done.complete(it) }
                background = null; normal = null; selected = null
                if (done.await()) {
                    visible = true
                    generation = currentGeneration
                    if (read) { previousRevision = currentRevision; previousOrigin = offset }
                    previousMotion = motion
                    previousVelocity = velocity
                }
            } catch (error: CancellationException) {
                throw error
            } catch (error: Exception) {
                failed = true
                android.util.Log.w("NavigationGlass", "Capture unavailable", error)
            } finally {
                background?.recycle(); normal?.recycle(); selected?.recycle()
            }
        }
    }
    Box(modifier.onGloballyPositioned { origin = it.positionInRoot(); size = it.size }) {
        AndroidView(factory = { view }, modifier = Modifier.fillMaxSize())
        if (!visible || failed) Box(Modifier.fillMaxSize().drawWithContent {
            val bar = Rect(20.dp.toPx(), 15.dp.toPx(), size.width - 20.dp.toPx(), size.height - 15.dp.toPx())
            drawRoundRect(if (dark) Color(0xFF202020) else Color(0xFFF5F5F5), bar.topLeft, bar.size,
                CornerRadius(bar.height / 2))
            if (dark) {
                val rimWidth = .75.dp.toPx()
                drawRoundRect(Color.White.copy(alpha = .28f), bar.topLeft + Offset(rimWidth / 2, rimWidth / 2),
                    Size(bar.width - rimWidth, bar.height - rimWidth), CornerRadius((bar.height - rimWidth) / 2),
                    style = Stroke(rimWidth))
            }
            val slot = (bar.width - 8.dp.toPx()) / count
            val pill = Rect(bar.left + 4.dp.toPx() + slot * position, bar.top + 4.dp.toPx(),
                bar.left + 4.dp.toPx() + slot * (position + 1), bar.bottom - 4.dp.toPx())
            val mask = Path().apply { addRoundRect(RoundRect(pill, CornerRadius(pill.height / 2))) }
            drawRoundRect((if (dark) Color.White else Color.Black).copy(alpha = 38f / 255),
                pill.topLeft, pill.size, CornerRadius(pill.height / 2))
            clipPath(mask, ClipOp.Difference) { drawLayer(labels) }
            clipPath(mask) { drawLayer(activeLabels) }
        })
    }
}

internal fun navigationMaterialJelly(velocity: Float, pressure: Float): FloatArray {
    val distortion = if (velocity.isFinite()) (kotlin.math.abs(velocity) / 10).coerceIn(0f, 1f) * .56f * pressure else 0f
    return floatArrayOf(1 - distortion * .5f, 1 + distortion * .3f)
}
