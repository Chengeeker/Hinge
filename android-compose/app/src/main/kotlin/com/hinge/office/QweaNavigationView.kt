package com.hinge.office

import android.content.Context
import android.graphics.*
import android.view.View
import android.widget.FrameLayout
import com.example.liquidglass.GlassMaterial
import com.example.liquidglass.EdgeHighlightEffect
import com.example.liquidglass.LiquidGlassView
import kotlin.math.roundToInt

// Compose owns gestures and semantics. This view only paints the API 24–32 material.
internal class QweaNavigationView(context: Context) : FrameLayout(context) {
    private var source: Bitmap? = null
    private var labels: Bitmap? = null
    private var activeLabels: Bitmap? = null
    private var composed: Bitmap? = null
    private var bodyFrame: Bitmap? = null
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
    private val rim = EdgeHighlightEffect()
    private val navigationRim = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    private val navigationRimBounds = RectF()
    private var rimTop = -1
    private var rimBottom = -1
    private val pill = RectF()
    private val clip = Path()
    private var dark = false
    private var clear = false
    private var press = 0f
    private var closed = false
    private var dirty = true
    private var bodyDirty = true
    var failure: ((String) -> Unit)? = null
    var surfaceGeneration = 0
        private set
    val ready get() = isAttachedToWindow && width > 0 && height > 0 && !closed

    private val body = glass()
    private val water = glass()

    init {
        clipChildren = false
        clipToPadding = false
        importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO_HIDE_DESCENDANTS
        addView(body)
        addView(water)
        body.setCustomBackdropCapture { capture(body, false) }
        water.setCustomBackdropCapture { capture(water, true) }
        water.enableBackdropBlur = false
    }

    private fun glass() = LiquidGlassView(context).apply {
        useShaderPipeline = false
        enableDynamicBackground = false
        enablePressEffect = false
        enableSensorHighlight = false
        enableAdaptiveTint = false
        enableShadow = false
        enableEdgeHighlight = false
        collectFrameStats = false
        material = GlassMaterial.CLEAR
        saturation = 100f
        aberrationIntensity = .4f
        displacementScale = 12f
        cornerRadius = 9999f
        isClickable = false
    }

    private fun drawGlass(glass: LiquidGlassView, canvas: Canvas) {
        glass.draw(canvas)
        // The library's overLight rim also darkens the entire fill; retain only its edge lighting.
        rim.draw(canvas, RectF(0f, 0f, glass.width.toFloat(), glass.height.toFloat()),
            FloatArray(8) { minOf(glass.width, glass.height) / 2f }, overLight = false,
            borderWidth = .5f * resources.displayMetrics.density)
    }

    private fun capture(glass: View, includeSurface: Boolean): Bitmap? {
        val background = source ?: return null
        if (glass.width == 0 || glass.height == 0) return null
        // Already half resolution: do not allocate a full screenshot then shrink it again.
        return Bitmap.createBitmap((glass.width + 1) / 2, (glass.height + 1) / 2, Bitmap.Config.ARGB_8888).also {
            Canvas(it).apply {
                scale(it.width.toFloat() / glass.width, it.height.toFloat() / glass.height)
                translate(-glass.left.toFloat(), -glass.top.toFloat())
                drawBitmap(background, null, Rect(0, 0, this@QweaNavigationView.width, this@QweaNavigationView.height), paint)
                if (includeSurface) composed?.let { surface -> drawBitmap(surface, 0f, 0f, paint) }
            }
        }
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        composed?.recycle()
        composed = null
        bodyFrame?.recycle()
        bodyFrame = null
        bodyDirty = true
        surfaceGeneration++
        dirty = true
    }

    override fun onLayout(changed: Boolean, l: Int, t: Int, r: Int, b: Int) = Unit

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        surfaceGeneration++
    }

    fun render(
        background: Bitmap?, labels: Bitmap?, selected: Float, pressed: Float,
        dark: Boolean, density: Float, lensVelocity: Float = 0f,
        selectedLabels: Bitmap? = null, generation: Int = surfaceGeneration,
        translucent: Boolean = false,
        count: Int = 3,
        complete: (Boolean) -> Unit,
    ) {
        if (!ready || generation != surfaceGeneration) {
            background?.recycle(); labels?.recycle(); selectedLabels?.recycle()
            complete(false)
            return
        }
        bodyDirty = bodyDirty || background != null || this.dark != dark || clear != translucent
        background?.let { source?.recycle(); source = it }
        labels?.let { this.labels?.recycle(); this.labels = it }
        selectedLabels?.let { activeLabels?.recycle(); activeLabels = it }
        this.dark = dark
        body.overLight = !dark
        water.overLight = !dark
        clear = translucent
        press = pressed
        val barWidth = width - 40 * density
        val slot = (barWidth - 8 * density) / count
        val jelly = navigationMaterialJelly(lensVelocity, pressed)
        val pw = (slot * (1 + .06f * pressed) + 24 * density * pressed) * jelly[0]
        val ph = ((height - 38 * density) * (1 + .06f * pressed) + 16 * density * pressed) * jelly[1]
        val cx = 24 * density + slot * (selected + .5f)
        pill.set(cx - pw / 2, height / 2f - ph / 2, cx + pw / 2, height / 2f + ph / 2)
        body.layout((20 * density).roundToInt(), (15 * density).roundToInt(), width - (20 * density).roundToInt(), height - (15 * density).roundToInt())
        water.layout(pill.left.roundToInt(), pill.top.roundToInt(), pill.right.roundToInt(), pill.bottom.roundToInt())
        navigationRim.strokeWidth = .75f * density
        val rimInset = navigationRim.strokeWidth / 2f
        navigationRimBounds.set(body.left + rimInset, body.top + rimInset, body.right - rimInset, body.bottom - rimInset)
        if (rimTop != body.top || rimBottom != body.bottom) {
            rimTop = body.top; rimBottom = body.bottom
            navigationRim.shader = android.graphics.LinearGradient(0f, rimTop.toFloat(), 0f, rimBottom.toFloat(),
                Color.argb(115, 255, 255, 255), Color.argb(31, 255, 255, 255), android.graphics.Shader.TileMode.CLAMP)
        }
        body.glassTint = Color.argb(
            if (!translucent) 191 else if (dark) 102 else 107,
            if (dark) 0 else if (translucent) 245 else 255,
            if (dark) 0 else if (translucent) 245 else 255,
            if (dark) 0 else if (translucent) 245 else 255,
        )
        body.blurAmount = if (translucent && !dark) .125f else 0f
        water.aberrationIntensity = 2f * pressed
        water.displacementScale = 30f * density * pressed
        dirty = true
        invalidate()
        complete(true)
    }

    override fun dispatchDraw(canvas: Canvas) {
        if (closed || source == null || width == 0 || height == 0) return
        try {
            val surface = composed ?: Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888).also { composed = it }
            if (dirty) {
                surface.eraseColor(Color.TRANSPARENT)
                val target = Canvas(surface)
                val base = bodyFrame ?: Bitmap.createBitmap(body.width, body.height, Bitmap.Config.ARGB_8888).also { bodyFrame = it }
                if (bodyDirty) {
                    base.eraseColor(Color.TRANSPARENT)
                    // Force fresh pixels only for an actual source/material update, never a free-running loop.
                    body.enableDynamicBackground = true
                    try { drawGlass(body, Canvas(base)) } finally { body.enableDynamicBackground = false }
                    bodyDirty = false
                }
                target.drawBitmap(base, body.left.toFloat(), body.top.toFloat(), paint)
                if (dark) target.drawRoundRect(navigationRimBounds, navigationRimBounds.height() / 2,
                    navigationRimBounds.height() / 2, navigationRim)
                paint.color = if (dark) Color.WHITE else Color.BLACK
                paint.alpha = ((if (clear && !dark) .08f else 38f / 255) * (1f - press) * 255).roundToInt()
                target.drawRoundRect(pill, pill.height() / 2, pill.height() / 2, paint)
                paint.alpha = 255
                clip.reset()
                clip.addRoundRect(pill, pill.height() / 2, pill.height() / 2, Path.Direction.CW)
                target.save()
                @Suppress("DEPRECATION")
                target.clipPath(clip, Region.Op.DIFFERENCE)
                labels?.let { target.drawBitmap(it, 0f, 0f, paint) }
                target.restore()
                target.save()
                target.clipPath(clip)
                activeLabels?.let { target.drawBitmap(it, 0f, 0f, paint) }
                target.restore()
                if (press > .001f) {
                    val checkpoint = target.saveLayerAlpha(pill, (press * 255).roundToInt())
                    target.translate(water.left.toFloat(), water.top.toFloat())
                    water.enableDynamicBackground = true
                    try { drawGlass(water, target) } finally { water.enableDynamicBackground = false }
                    target.restoreToCount(checkpoint)
                }
                dirty = false
            }
            canvas.drawBitmap(surface, 0f, 0f, paint)
        } catch (error: Exception) {
            close()
            failure?.invoke(error.javaClass.simpleName)
        } catch (error: LinkageError) {
            close()
            failure?.invoke(error.javaClass.simpleName)
        }
    }

    fun close() {
        if (closed) return
        closed = true
        source?.recycle(); source = null
        labels?.recycle(); labels = null
        activeLabels?.recycle(); activeLabels = null
        composed?.recycle(); composed = null
        bodyFrame?.recycle(); bodyFrame = null
        rim.cleanup()
        removeAllViews()
    }
}
