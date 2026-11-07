@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
package com.hinge.office

import android.graphics.BitmapFactory
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.boundsInWindow
import androidx.compose.ui.layout.onGloballyPositioned
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.CancellationException
import java.time.Instant
import java.time.LocalDate
import java.time.YearMonth
import java.time.ZoneId
import java.text.DateFormat
import java.util.Date
import kotlin.math.ceil
import kotlin.math.roundToInt

@Composable fun PermissionCard(model:HingeModel, icon:String,title:String,message:String,request:String,refresh:()->Unit) = OriginalCard(color=MaterialTheme.colorScheme.surfaceContainerHigh) {
    Row(Modifier.padding(18.dp),verticalAlignment=Alignment.CenterVertically) {
        HIcon(icon,color=MaterialTheme.colorScheme.primary); Spacer(Modifier.width(14.dp))
        Column(Modifier.weight(1f)){Text(title);Text(message)}
        Row(horizontalArrangement=Arrangement.spacedBy(8.dp)) {
            HingeOutlinedButton(onClick={model.run{model.api.call("openAppSettings")}}){Text("系统设置")}
            HingeFilledButton(onClick={model.run{model.api.call(request);refresh()}}){Text("授予权限")}
        }
    }
}

@Composable fun OriginalCalendar(model:HingeModel) {
    var rows by remember { mutableStateOf<List<Map<String,Any?>>>(emptyList()) }
    var allowed by remember { mutableStateOf<Boolean?>(null) }
    var revision by remember { mutableIntStateOf(0) }
    var selected by remember { mutableStateOf(LocalDate.now()) }
    var month by remember { mutableStateOf(YearMonth.now()) }
    var loading by remember { mutableStateOf(false) }
    var failure by remember { mutableStateOf("") }
    LaunchedEffect(revision,model.foreground) {
        loading=true;failure=""
        try { allowed=model.api.call("hasCalendarPermission")==true;if(allowed==true)rows=model.api.call("calendarEvents").asRows() }
        catch(e:CancellationException){throw e} catch(e:Exception){failure=e.message.orEmpty()} finally{loading=false}
    }
    val events=remember(rows){rows.groupBy{Instant.ofEpochMilli(it.number("start")).atZone(ZoneId.systemDefault()).toLocalDate()}}
    OriginalPage(model.settings["floatingCapsuleNavigation"]==true) {
        Text("按日期查看手机日程",style=MaterialTheme.typography.bodyLarge);Spacer(Modifier.height(12.dp))
        if(allowed==false) PermissionCard(model,"Symbols.calendar_month_rounded","需要日历读取权限","只读系统日程，不会修改手机日历。","requestCalendarPermission"){revision++}
        if(failure.isNotEmpty())OriginalError("读取日历失败",failure)
        if(allowed!=false) {
            OriginalCard {
                Column(Modifier.padding(start=16.dp,top=14.dp,end=16.dp,bottom=16.dp)) {
                    Row(verticalAlignment=Alignment.CenterVertically) {
                        Text("${month.year}年${month.monthValue}月",style=MaterialTheme.typography.titleLarge,modifier=Modifier.weight(1f))
                        IconButton(onClick={month=month.minusMonths(1)}){HIcon("Symbols.chevron_left_rounded","上个月")}
                        IconButton(onClick={selected=LocalDate.now();month=YearMonth.from(selected)}){HIcon("Symbols.today_rounded","回到今天")}
                        IconButton(onClick={month=month.plusMonths(1)}){HIcon("Symbols.chevron_right_rounded","下个月")}
                    }
                    Spacer(Modifier.height(6.dp));Row { listOf("一","二","三","四","五","六","日").forEach{Box(Modifier.weight(1f),contentAlignment=Alignment.Center){Text(it,style=MaterialTheme.typography.labelMedium)}} }
                    Spacer(Modifier.height(6.dp))
                    val first=month.atDay(1).minusDays((month.atDay(1).dayOfWeek.value-1).toLong())
                    repeat(6){week->Row{repeat(7){day->
                        val date=first.plusDays((week*7+day).toLong());val chosen=date==selected;val today=date==LocalDate.now();val scheme=MaterialTheme.colorScheme
                        val background by androidx.compose.animation.animateColorAsState(if(chosen)scheme.primary else Color.Transparent,androidx.compose.animation.core.tween(160,easing=androidx.compose.animation.core.LinearEasing),label="calendar-day")
                        Box(Modifier.weight(1f).height(48.dp).padding(3.dp).clip(RoundedCornerShape(16.dp)).background(background)
                            .then(if(today&&!chosen)Modifier.border(1.5.dp,scheme.primary,RoundedCornerShape(16.dp)) else Modifier).clickable{selected=date;month=YearMonth.from(date)},contentAlignment=Alignment.Center){
                            Column(horizontalAlignment=Alignment.CenterHorizontally){Text("${date.dayOfMonth}",color=if(chosen)scheme.onPrimary else if(date.month==month.month)scheme.onSurface else scheme.onSurfaceVariant.copy(alpha=.45f),fontWeight=if(chosen||today)FontWeight.W700 else null)
                                Spacer(Modifier.height(3.dp));Box(Modifier.size(5.dp).background(if(events.containsKey(date))if(chosen)scheme.onPrimary else scheme.primary else Color.Transparent,CircleShape))}
                        }
                    }}}
                    if(loading)HingeLinearProgressIndicator(Modifier.fillMaxWidth())
                }
            }
            Spacer(Modifier.height(16.dp));OriginalCard { Column(Modifier.padding(20.dp)) {
                Text("${selected.monthValue}月${selected.dayOfMonth}日的日程",style=MaterialTheme.typography.titleLarge);Spacer(Modifier.height(10.dp))
                val list=events[selected].orEmpty()
                if(list.isEmpty())Text("这一天没有日程")
                list.forEach{row->OriginalCard(Modifier.padding(bottom=10.dp)){Row(Modifier.heightIn(min=72.dp).padding(start=16.dp,end=24.dp,top=8.dp,bottom=8.dp),verticalAlignment=Alignment.CenterVertically){
                    Box(Modifier.size(40.dp).background(MaterialTheme.colorScheme.primaryContainer,CircleShape),contentAlignment=Alignment.Center){HIcon("Symbols.event_rounded",color=MaterialTheme.colorScheme.primary)}
                    Spacer(Modifier.width(16.dp));Column{
                        Text(row.text("title"),style=MaterialTheme.typography.bodyLarge)
                        val start=if(row["allDay"]==true)"全天" else java.text.SimpleDateFormat("M月d日 HH:mm",java.util.Locale.CHINA).format(Date(row.number("start")))
                        val end=if(row["allDay"]==true||row.number("end")==0L)"" else " - ${java.text.SimpleDateFormat("M月d日 HH:mm",java.util.Locale.CHINA).format(Date(row.number("end")))}"
                        Text(start+end+row.text("location").let{if(it.isEmpty())"" else "\n$it"},style=MaterialTheme.typography.bodyMedium)
                    }
                }}}
            } }
        }
    }
}

@Composable fun PhotoImage(model:HingeModel,uri:String,modifier:Modifier=Modifier,preview:Boolean=false,albumCover:Boolean=false) {
    var requested by remember(uri,preview){mutableStateOf(preview)}
    var finished by remember(uri,preview){mutableStateOf(false)}
    val bitmap by key(uri,preview) {
        produceState<android.graphics.Bitmap?>(null,requested){
            if(!requested)return@produceState
            try{val bytes=model.api.call(if(preview)"photoPreviewBytes" else "photoThumbnailBytes",mapOf("uri" to uri)) as? ByteArray
                value=withContext(Dispatchers.Default){bytes?.let{BitmapFactory.decodeByteArray(it,0,it.size)}}
            }catch(e:CancellationException){throw e}catch(_:Exception){}finally{finished=true}
        }
    }
    val visibility = if (requested) Modifier else Modifier.onGloballyPositioned {
        val bounds = it.boundsInWindow()
        if (bounds.width > 0f && bounds.height > 0f) requested = true
    }
    Box(modifier.then(visibility).background(if(preview)Color.Transparent else MaterialTheme.colorScheme.surfaceContainerHighest),contentAlignment=Alignment.Center){
        if(bitmap==null){if(preview){if(!finished)HingeCircularProgressIndicator() else Text("图片预览读取失败",color=MaterialTheme.colorScheme.error)}else HIcon(if(albumCover)"Symbols.photo_library_rounded" else "Symbols.photo_rounded",size=40.dp,color=MaterialTheme.colorScheme.onSurfaceVariant)}
        else Image(bitmap!!.asImageBitmap(),null,Modifier.fillMaxSize(),contentScale=if(preview)ContentScale.Fit else ContentScale.Crop)
    }
}

@Composable fun OriginalPhotos(model:HingeModel) {
    var albums by remember{mutableStateOf<List<Map<String,Any?>>>(emptyList())}
    var photos by remember{mutableStateOf<List<Map<String,Any?>>>(emptyList())}
    var album by remember{mutableStateOf<Map<String,Any?>?>(null)}
    var allowed by remember{mutableStateOf<Boolean?>(null)}
    var total by remember{mutableIntStateOf(0)};var revision by remember{mutableIntStateOf(0)}
    var loading by remember{mutableStateOf(false)};var error by remember{mutableStateOf("")}
    var preview by remember{mutableStateOf<Map<String,Any?>?>(null)}
    val scroll=rememberScrollState()
    var generation by remember{mutableIntStateOf(0)}
    suspend fun load(reset:Boolean){
        if(loading&&!reset)return
        val request=++generation;val target=album
        loading=true;error=""
        if(reset){photos=emptyList();total=0}
        try{
            val permission=model.api.call("hasPhotosPermission")==true
            if(request!=generation)return
            allowed=permission
            if(permission){
                if(target==null){val result=model.api.call("photoAlbums").asRows();if(request==generation)albums=result}
                else{val page=model.api.call("photosPage",mapOf("albumId" to target.text("id"),"offset" to if(reset)0 else photos.size,"limit" to 100)).asMap();if(request==generation){photos=((if(reset)emptyList()else photos)+page["items"].asRows()).distinctBy{it.text("uri")};total=page.number("total").toInt()}}
            }
        }catch(e:CancellationException){throw e}catch(e:Exception){if(request==generation)error=e.message.orEmpty()}finally{if(request==generation)loading=false}
    }
    LaunchedEffect(album,revision,model.foreground){scroll.scrollTo(0);load(true)}
    LaunchedEffect(album){snapshotFlow{album!=null&&photos.size<total&&!loading&&scroll.maxValue-scroll.value<900}.distinctUntilChanged().collect{if(it)load(false)}}
    BackHandler(album!=null){album=null}
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val available=minOf(maxWidth.value-32,1180f)
        val density=androidx.compose.ui.platform.LocalDensity.current
        val safeBottom=with(density){WindowInsets.navigationBars.getBottom(this).toDp()}
        Column(Modifier.fillMaxSize().verticalScroll(scroll).padding(start=((maxWidth.value-available)/2).dp,end=((maxWidth.value-available)/2).dp,top=8.dp,bottom=if(model.settings["floatingCapsuleNavigation"]==true)136.dp+safeBottom else 32.dp)) {
            Row(verticalAlignment=Alignment.CenterVertically){if(album!=null)IconButton(onClick={album=null}){HIcon("Symbols.arrow_back_rounded","返回相册集")};Column(Modifier.weight(1f)){OriginalSection(album?.text("name")?:"相册集",if(album==null)"先选择相册，再查看其中的图片；不会默认铺开全部照片" else "共 ${album!!.number("count")} 张图片")}}
            Spacer(Modifier.height(12.dp))
            if(allowed==false)PermissionCard(model,"Symbols.photo_library_rounded","需要照片读取权限","只读取图片列表和缩略图，不会上传照片。","requestPhotosPermission"){revision++}
            if(error.isNotEmpty())OriginalError("读取相册失败",error)
            if(allowed!=false){
                val data=if(album==null)albums else photos
                if(data.isEmpty()&&!loading)OriginalEmpty(if(album==null)"Symbols.photo_library_rounded" else "Symbols.photo_rounded",if(album==null)"暂无相册集" else "相册为空",if(album==null)"手机媒体库中没有可读取的图片或相册。" else "这个相册中暂时没有可读取的图片。")
                else if(album==null){
                    val columns=ceil(available/(280+12)).toInt().coerceAtLeast(1)
                    albums.chunked(columns).forEach{group->Row(horizontalArrangement=Arrangement.spacedBy(12.dp)){
                        group.forEach{entry->Surface(Modifier.weight(1f).height(250.dp).clickable{album=entry},shape=RoundedCornerShape(20.dp),color=MaterialTheme.colorScheme.surfaceContainer){Column{
                            PhotoImage(model,entry.text("coverUri"),Modifier.fillMaxWidth().weight(1f),albumCover=true);Row(Modifier.padding(start=14.dp,top=10.dp,end=14.dp,bottom=12.dp),verticalAlignment=Alignment.CenterVertically){Text(entry.text("name"),Modifier.weight(1f),maxLines=1,overflow=TextOverflow.Ellipsis,style=MaterialTheme.typography.titleMedium);Spacer(Modifier.width(8.dp));Text("${entry.number("count")} 张")}
                        }}};repeat(columns-group.size){Spacer(Modifier.weight(1f))}
                    };Spacer(Modifier.height(12.dp))}
                }else{
                    val count=if(available<520)2 else (available/240).roundToInt().coerceIn(2,5)
                    val width=(available-12*(count-1))/count;val heights=FloatArray(count);val columns=List(count){mutableListOf<Map<String,Any?>>()} 
                    fun ratio(row:Map<String,Any?>)=if(row.number("width")>0&&row.number("height")>0)(row.number("width").toFloat()/row.number("height")).coerceIn(.72f,1.55f) else 1.15f
                    photos.forEach{row->val target=heights.indices.minBy{heights[it]};columns[target].add(row);heights[target]+=width/ratio(row)+70}
                    Row(horizontalArrangement=Arrangement.spacedBy(12.dp)){columns.forEach{column->Column(Modifier.weight(1f)){column.forEach{row->
                        Surface(Modifier.fillMaxWidth().clickable{preview=row},shape=RoundedCornerShape(20.dp),color=MaterialTheme.colorScheme.surfaceContainer){Column{PhotoImage(model,row.text("uri"),Modifier.fillMaxWidth().height((width/ratio(row)).dp));Text(row.text("name"),Modifier.padding(start=12.dp,top=8.dp,end=12.dp,bottom=10.dp),maxLines=1,overflow=TextOverflow.Ellipsis)}}
                    }}}}
                }
            }
        }
    }
    preview?.let{photo->PhotoPreview(model,photo){preview=null}}
}

@Composable fun PhotoPreview(model:HingeModel,photo:Map<String,Any?>,dismiss:()->Unit){
    var metadata by remember{mutableStateOf<Map<String,Any?>>(emptyMap())}
    var scale by remember{mutableFloatStateOf(1f)};var x by remember{mutableFloatStateOf(0f)};var y by remember{mutableFloatStateOf(0f)}
    LaunchedEffect(photo){try{metadata=model.api.call("mediaMetadata",mapOf("uri" to photo.text("uri"))).asMap()}catch(_:Exception){}}
    Dialog(onDismissRequest=dismiss,properties=DialogProperties(usePlatformDefaultWidth=false)){
        Surface(Modifier.padding(horizontal=40.dp,vertical=24.dp).widthIn(max=820.dp).hingeWindowTapFeedback(),shape=RoundedCornerShape(28.dp),color=MaterialTheme.colorScheme.surfaceContainerHigh){Column(Modifier.padding(20.dp)){
            Row(verticalAlignment=Alignment.CenterVertically){Text(photo.text("name"),Modifier.weight(1f),maxLines=1,overflow=TextOverflow.Ellipsis,style=MaterialTheme.typography.titleLarge);IconButton(onClick=dismiss){HIcon("Symbols.close_rounded","关闭预览")}}
            Spacer(Modifier.height(12.dp));Box(Modifier.weight(1f,false).fillMaxWidth().clip(RoundedCornerShape(0.dp)).pointerInput(Unit){detectTransformGestures{_,pan,zoom,_->scale=(scale*zoom).coerceIn(1f,5f);x=(x+pan.x).coerceIn(-size.width*(scale-1)/2,size.width*(scale-1)/2);y=(y+pan.y).coerceIn(-size.height*(scale-1)/2,size.height*(scale-1)/2)}}){PhotoImage(model,photo.text("uri"),Modifier.fillMaxSize().graphicsLayer{scaleX=scale;scaleY=scale;translationX=x;translationY=y},true)}
            Spacer(Modifier.height(14.dp));val width=metadata.number("width",photo.number("width"));val height=metadata.number("height",photo.number("height"));val camera=listOf(metadata.text("cameraMake"),metadata.text("cameraModel")).filter{it.isNotBlank()}.joinToString(" ")
            Text(listOf("尺寸：${if(width>0)"$width × $height" else "未知"}","拍摄时间：${if(photo.number("takenAt")<=0)"日期未知" else java.text.SimpleDateFormat("yyyy/M/d",java.util.Locale.CHINA).format(Date(photo.number("takenAt")))}",if(camera.isNotEmpty())"设备：$camera" else "",metadata.text("dateTimeOriginal").let{if(it.isNotEmpty())"原始时间：$it" else ""},"媒体地址：${photo.text("uri")}").filter{it.isNotEmpty()}.joinToString("\n"),style=MaterialTheme.typography.bodySmall)
            Spacer(Modifier.height(8.dp));Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.End){HingeOutlinedButton(onClick={val peer=model.ready.firstOrNull();if(peer==null)model.message="请先连接电脑" else {model.sendUri(photo.text("uri"),photo.text("name"),photo.text("mimeType","image/*"),peer.text("deviceId"));dismiss()}}){HIcon("Symbols.send_rounded");Spacer(Modifier.width(8.dp));Text("发送")};Spacer(Modifier.width(10.dp));HingeFilledButton(onClick=dismiss){Text("完成")}}
        }}
    }
}
