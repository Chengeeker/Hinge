package com.hinge.office

import android.os.Handler
import android.os.Looper
import android.view.FrameMetrics
import android.view.Window
import androidx.compose.runtime.*

class FrameSamples{
    private val frames=ArrayDeque<Pair<Long,Long>>()
    fun add(now:Long,span:Long){if(span<0||frames.lastOrNull()?.first?.let{now<=it}==true)return;frames.addLast(now to span);while(frames.size>512||(frames.size>2&&frames.elementAt(1).first<now-1000))frames.removeFirst()}
    fun p95(now:Long):Double?{if(frames.size<2||now-frames.last().first>3000)return null;val sorted=frames.map{it.second}.sorted();return sorted[(kotlin.math.ceil(sorted.size*.95).toInt()-1)]/1000000.0}
}
@Composable fun desktopStatistics(model:HingeModel,enabled:Boolean):String{
    val samples=remember{FrameSamples()};var result by remember{mutableStateOf("")}
    DisposableEffect(enabled){
        val listener=Window.OnFrameMetricsAvailableListener{_,metrics,_->samples.add(android.os.SystemClock.elapsedRealtime(),metrics.getMetric(FrameMetrics.TOTAL_DURATION))}
        if(enabled)model.activity.window.addOnFrameMetricsAvailableListener(listener,Handler(Looper.getMainLooper()))
        onDispose{if(enabled)model.activity.window.removeOnFrameMetricsAvailableListener(listener)}
    }
    var previous by remember{mutableStateOf<Map<String,Any?>>(emptyMap())}
    LaunchedEffect(model.desktop["timeMs"],enabled){if(enabled){val event=model.desktop;val time=event.number("timeMs");val delta=time-previous.number("timeMs")
        if(previous.isNotEmpty()&&delta>0){
            fun number(key:String)=(event[key] as? Number)?.toDouble()
            fun gap(key:String)=number(key)?.takeIf{it>0}?.let{"%.1f".format(it)}?:"—"
            val fps=(event.number("presented")-previous.number("presented"))*1000.0/delta
            val texture=if(event["consumed"]!=null&&previous["consumed"]!=null&&event["textureSampling"]!=false)"%.0f FPS".format((event.number("consumed")-previous.number("consumed"))*1000.0/delta)else "待采样"
            val p95=samples.p95(android.os.SystemClock.elapsedRealtime())?.let{"%.1f".format(it)}?:"—"
            val refresh=number("displayHz")?.takeIf{it>0}?.let{"%.0f Hz".format(it)}?:"未知"
            val decoder=event.text("decoderName");val mode=when(event.text("decoderMode")){"hardware"->"硬解";"software"->"软解";else->"解码器"}
            result="视频输出 ${"%.0f".format(fps)} FPS · 纹理取帧 $texture\n视频最大间隔 ${gap("maxVideoGapMs")} ms · 取帧最大间隔 ${gap("maxTextureGapMs")} ms\n屏幕 $refresh · 界面重建 P95 $p95 ms"+(if(decoder.isEmpty())"" else "\n$mode · 待解码 ${event.number("pending")} · $decoder")
        };previous=event
    }else{previous=emptyMap();result=""}}
    return result
}
