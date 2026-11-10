package com.hinge.office
import androidx.compose.ui.unit.*
internal fun hingeAppFontScale(value: Any?): Float = (value as? Number)?.toFloat()?.takeIf { it.isFinite() }?.coerceIn(.85f, 1.5f) ?: 1f
internal fun hingeFontDensity(base: Density, scale: Float): Density = object : Density by base {
    override val fontScale = base.fontScale * scale
    override fun TextUnit.toDp(): Dp = with(base) { this@toDp.toDp() } * scale
    override fun Dp.toSp(): TextUnit = with(base) { (this@toSp / scale).toSp() }
}
