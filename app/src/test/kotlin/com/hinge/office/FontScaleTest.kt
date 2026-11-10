package com.hinge.office
import androidx.compose.ui.unit.*
import org.junit.Assert.*
import org.junit.Test
class FontScaleTest {
 @Test fun fractionRangeAndSystemScalingArePreserved() {
  assertEquals(1f,hingeAppFontScale(null)); assertEquals(1f,hingeAppFontScale(Float.NaN))
  assertEquals(1.15f,hingeAppFontScale(1.15)); assertEquals(1.5f,hingeAppFontScale(2f))
  val base=Density(2f,1.3f); val scaled=hingeFontDensity(base,1.5f)
  val original=with(base){16.sp.toDp()}; val changed=with(scaled){16.sp.toDp()}
  assertEquals(original.value*1.5f,changed.value,.001f)
  assertEquals(16f,with(scaled){changed.toSp()}.value,.001f)
 }
}
