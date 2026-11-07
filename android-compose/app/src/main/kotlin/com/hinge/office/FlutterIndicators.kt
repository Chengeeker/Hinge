package com.hinge.office

import androidx.compose.animation.core.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.semantics.*
import androidx.compose.ui.unit.*

// Flutter's retained theme uses the 2023 indicators (no endpoint dot or track gap).
internal fun flutterLinearSegments(milliseconds:Float):List<Pair<Float,Float>> {
    val t=milliseconds%1800f
    fun point(start:Float,duration:Float,x1:Float,x2:Float)=CubicBezierEasing(x1,0f,x2,1f).transform(((t-start)/duration).coerceIn(0f,1f))
    return listOf(point(333f,750f,.4f,1f) to point(0f,750f,.2f,.8f),point(1267f,533f,.1f,.45f) to point(1000f,567f,0f,.65f))
}
internal fun flutterCircularArc(milliseconds:Float):Pair<Float,Float>{
    val path=(milliseconds%1333f)/1333f
    val head=FastOutSlowInEasing.transform((path*2).coerceIn(0f,1f))
    val tail=FastOutSlowInEasing.transform((path*2-1).coerceIn(0f,1f))
    return (-90f+tail*270f+(milliseconds%2222f)/2222f*360f+path*90f) to maxOf((head-tail)*270f,.001f*180f/kotlin.math.PI.toFloat())
}
@Composable private fun animationMillis(duration:Int):Float {
    val transition=rememberInfiniteTransition(label="flutter-progress")
    return transition.animateFloat(0f,duration.toFloat(),infiniteRepeatable(tween(duration,easing=LinearEasing)),label="phase").value
}
@Composable fun HingeLinearProgressIndicator(modifier:Modifier=Modifier,color:Color=MaterialTheme.colorScheme.primary,trackColor:Color=MaterialTheme.colorScheme.secondaryContainer){
    val time=animationMillis(1800)
    val rtl=LocalLayoutDirection.current==LayoutDirection.Rtl
    Canvas(modifier.fillMaxWidth().height(4.dp).semantics{progressBarRangeInfo=ProgressBarRangeInfo.Indeterminate}){
        drawRect(trackColor)
        for((tail,head)in flutterLinearSegments(time))if(head>tail)drawRect(color,Offset((if(rtl)1-head else tail)*size.width,0f),Size((head-tail)*size.width,size.height))
    }
}
@Composable fun HingeLinearProgressIndicator(progress:()->Float,modifier:Modifier=Modifier,color:Color=MaterialTheme.colorScheme.primary,trackColor:Color=MaterialTheme.colorScheme.secondaryContainer){
    val rtl=LocalLayoutDirection.current==LayoutDirection.Rtl
    Canvas(modifier.fillMaxWidth().height(4.dp).semantics{progressBarRangeInfo=ProgressBarRangeInfo(progress().coerceIn(0f,1f),0f..1f)}){
        val fraction=progress().coerceIn(0f,1f);drawRect(trackColor);drawRect(color,Offset(if(rtl)size.width*(1-fraction)else 0f,0f),Size(size.width*fraction,size.height))
    }
}
@Composable fun HingeCircularProgressIndicator(modifier:Modifier=Modifier,color:Color=MaterialTheme.colorScheme.primary,strokeWidth:Dp=4.dp){
    val time=animationMillis(1333*2222)
    Canvas(modifier.size(36.dp).semantics{progressBarRangeInfo=ProgressBarRangeInfo.Indeterminate}){
        val (start,sweep)=flutterCircularArc(time)
        drawArc(color,start,sweep,false,style=Stroke(strokeWidth.toPx(),cap=StrokeCap.Square))
    }
}
