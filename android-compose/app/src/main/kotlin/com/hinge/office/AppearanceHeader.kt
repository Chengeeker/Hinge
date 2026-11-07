@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
package com.hinge.office

import android.graphics.Bitmap
import android.os.Build
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.drawWithCache
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.layer.drawLayer
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.unit.*
import kotlinx.coroutines.*
import java.util.concurrent.Executors

@Composable internal fun AppearanceHeader(model:HingeModel, source:GlassBackdrop, back:()->Unit) {
    val density=LocalDensity.current
    val layout=LocalLayoutDirection.current
    val scheme=MaterialTheme.colorScheme
    val crop=rememberGraphicsLayer()
    var viewport by remember { mutableStateOf(IntSize.Zero) }
    var legacyBlur by remember { mutableStateOf<Bitmap?>(null) }
    // Older Android cannot apply a RenderEffect; reuse the existing GLES Gaussian pass.
    if(Build.VERSION.SDK_INT<31) LaunchedEffect(source,viewport,density,scheme.surface) {
        if(viewport.width==0)return@LaunchedEffect
        val dispatcher=Executors.newSingleThreadExecutor{Thread(it,"Hinge-Header").apply{isDaemon=true}}.asCoroutineDispatcher()
        var renderer:GlassRenderer?=null
        try {
            withContext(dispatcher){renderer=GlassRenderer(model.activity)}
            var version=-1L
            while(isActive){
                withFrameNanos{}
                if(source.version==version||source.layer.size.width==0)continue
                version=source.version
                crop.record(density,layout,viewport){drawRect(scheme.surface);drawLayer(source.layer)}
                val snapshot=crop.toImageBitmap().asAndroidBitmap()
                val bitmap=if(snapshot.config==Bitmap.Config.ARGB_8888)snapshot else requireNotNull(snapshot.copy(Bitmap.Config.ARGB_8888,false))
                legacyBlur=withContext(dispatcher){requireNotNull(renderer).blurBackdrop(bitmap,20*density.density)}
            }
        }catch(e:CancellationException){throw e}
        catch(e:Exception){model.message="顶栏模糊渲染失败：${e.message}"}
        finally{withContext(NonCancellable+dispatcher){renderer?.close()};dispatcher.close()}
    }
    TopAppBar(expandedHeight=56.dp,modifier=Modifier.clipToBounds().drawWithCache {
        val capture=IntSize(size.width.toInt(),(size.height+60.dp.toPx()).toInt())
        if(Build.VERSION.SDK_INT>=31){
            crop.renderEffect=BlurEffect(20.dp.toPx(),20.dp.toPx(),TileMode.Clamp)
        }else if(viewport!=capture)viewport=capture
        val fade=Brush.verticalGradient(listOf(scheme.surface.copy(alpha=.95f),scheme.surface.copy(alpha=0f)))
        val lineY=size.height-.25.dp.toPx()
        onDrawWithContent {
            if(Build.VERSION.SDK_INT>=31){
                crop.record(size=capture){drawRect(scheme.surface);drawLayer(source.layer)}
                drawLayer(crop)
            }else legacyBlur?.let{drawImage(it.asImageBitmap())}
            drawRect(scheme.surface.copy(alpha=.45f))
            drawRect(fade)
            drawLine(scheme.outlineVariant.copy(alpha=.24f),Offset(0f,lineY),Offset(size.width,lineY),.5.dp.toPx())
            drawContent()
        }
    },title={Text("个性化",style=MaterialTheme.typography.titleLarge)},
        navigationIcon={IconButton(onClick=back){HIcon("Symbols.arrow_back_rounded","返回设置")}},
        colors=TopAppBarDefaults.topAppBarColors(containerColor=Color.Transparent))
}
