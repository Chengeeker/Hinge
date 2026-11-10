@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
package com.hinge.office

import android.graphics.BitmapFactory
import android.util.Base64
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@Composable fun EncodedAppIcon(encoded:String,notifications:Boolean=false){
    val bitmap by key(encoded) {
        produceState<android.graphics.Bitmap?>(null) {
            if (encoded.isEmpty()) return@produceState
            value = withContext(Dispatchers.Default) {
                runCatching {
                    val bytes = Base64.decode(encoded, Base64.DEFAULT)
                    BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
                }.getOrNull()
            }
        }
    }
    val size=if(notifications)44.dp else 40.dp
    bitmap?.let { Image(it.asImageBitmap(),null,Modifier.size(size).clip(RoundedCornerShape(10.dp))) }
        ?: Box(Modifier.size(size).background(MaterialTheme.colorScheme.primaryContainer,CircleShape),contentAlignment=Alignment.Center){HIcon(if(notifications)"Symbols.notifications_rounded" else "Symbols.apps_rounded",color=MaterialTheme.colorScheme.onPrimaryContainer)}
}

@Composable fun OriginalNotifications(model:HingeModel,back:()->Unit){
    var page by remember{mutableStateOf<Map<String,Any?>>(emptyMap())};var rows by remember{mutableStateOf<List<Map<String,Any?>>>(emptyList())}
    var ascending by remember{mutableStateOf(false)};var packageName by remember{mutableStateOf("")};var revision by remember{mutableIntStateOf(0)}
    var loading by remember{mutableStateOf(true)};var failure by remember{mutableStateOf("")};var clear by remember{mutableStateOf(false)};var sortMenu by remember{mutableStateOf(false)};var appsMenu by remember{mutableStateOf(false)}
    val locale=androidx.compose.ui.platform.LocalConfiguration.current.locales[0]
    val list=rememberLazyListState()
    var generation by remember{mutableIntStateOf(0)}
    suspend fun load(reset:Boolean){
        if(loading&&!reset)return
        val request=++generation;val sort=ascending;val filter=packageName
        loading=true;failure=""
        try{
            val access=model.api.call("notificationHistoryAccessEnabled")==true;val enabled=model.api.call("notificationHistoryEnabled")==true
            if(request!=generation)return
            if(!access||!enabled){page=mapOf("access" to access,"enabled" to enabled);rows=emptyList()}
            else{val result=model.api.call("notificationHistory",mapOf("offset" to if(reset)0 else rows.size,"limit" to 100,"ascending" to sort,"packageName" to filter)).asMap();if(request==generation){page=result;rows=((if(reset)emptyList()else rows)+result["items"].asRows()).distinctBy{it.text("id")}}}
        }catch(e:kotlinx.coroutines.CancellationException){throw e}catch(e:Exception){if(request==generation)failure=e.message.orEmpty()}finally{if(request==generation)loading=false}
    }
    fun enabled(value:Boolean)=model.run{
        if(value&&page["access"]!=true){model.api.call("openNotificationHistorySettings");model.message="请在系统设置中允许 Hinge 访问通知，返回后再开启采集"}
        else{model.api.call("setNotificationHistoryEnabled",mapOf("enabled" to value));model.message=if(value)"通知历史采集已开启" else "通知历史采集已关闭，已有记录仍保留";revision++}
    }
    LaunchedEffect(ascending,packageName,revision,model.foreground){delay(300);load(true)}
    DisposableEffect(Unit){val listener=model.api.listen("hinge/notification_history/events"){revision++};onDispose{listener.close()}}
    val nearEnd by remember{derivedStateOf{(list.layoutInfo.visibleItemsInfo.lastOrNull()?.index?:0)>=list.layoutInfo.totalItemsCount-5}}
    LaunchedEffect(nearEnd,rows.size){if(nearEnd&&rows.size<page.number("total"))load(false)}
    Scaffold(topBar={TopAppBar(expandedHeight = 56.dp, title={Text("通知历史",style=MaterialTheme.typography.titleLarge)},navigationIcon={IconButton(onClick=back){HIcon("Symbols.arrow_back_rounded","返回")}},actions={IconButton(onClick={clear=true},enabled=!loading&&page.number("total")>0){HIcon("Symbols.delete_rounded","清空通知历史")};IconButton(onClick={revision++},enabled=!loading){HIcon("Symbols.refresh_rounded","刷新")}})}){padding->
        if(loading&&rows.isEmpty())Box(Modifier.fillMaxSize().padding(padding),contentAlignment=Alignment.Center){HingeCircularProgressIndicator()}
        else LazyColumn(Modifier.padding(padding),state=list,contentPadding=PaddingValues(start=16.dp,top=8.dp,end=16.dp,bottom=32.dp)){
            if(page["access"]!=true)item{OriginalCard{Column(Modifier.padding(16.dp)){
                Row{HIcon("Symbols.notifications_active_rounded",color=MaterialTheme.colorScheme.primary);Spacer(Modifier.width(12.dp));Text("需要通知访问权限",style=MaterialTheme.typography.titleMedium)};Spacer(Modifier.height(8.dp));Text("Android 会把通知访问作为系统级授权。Hinge 只读取授权之后收到的通知，并把内容保存在应用私有数据库中；已消失的旧通知无法由系统完整恢复。");Spacer(Modifier.height(12.dp));HingeTonalButton(onClick={model.run{model.api.call("openNotificationHistorySettings")}}){HIcon("Symbols.settings_rounded");Spacer(Modifier.width(8.dp));Text("打开通知访问设置")}
            }}}
            else if(page["enabled"]!=true)item{OriginalCard{OriginalRow("收集应用通知","开启后记录新收到的应用通知，并同步给已连接的 Windows","Symbols.notifications_rounded",iconColor=MaterialTheme.colorScheme.primary,trailing={Switch(false,{enabled(it)})});Text("通知访问已允许，但历史采集仍处于关闭状态。",Modifier.padding(start=16.dp,end=16.dp,bottom=16.dp))}}
            else{
                item{OriginalCard{Row(Modifier.padding(horizontal=16.dp,vertical=12.dp),verticalAlignment=Alignment.CenterVertically){HIcon("Symbols.history_rounded",color=MaterialTheme.colorScheme.primary);Spacer(Modifier.width(12.dp));Text("共 ${page.number("total")} 条通知 · 已加载 ${rows.size} 条",Modifier.weight(1f),style=MaterialTheme.typography.bodyLarge);Switch(true,{enabled(it)})}};Spacer(Modifier.height(12.dp))}
                item{OriginalCard{Column(Modifier.padding(horizontal=12.dp,vertical=4.dp)){
                    Box{NotificationFilter("时间顺序",if(ascending)"时间（早到晚）" else "时间（晚到早）"){sortMenu=true};DropdownMenu(sortMenu,{sortMenu=false},modifier=Modifier.hingeWindowTapFeedback()){listOf(true,false).forEach{value->DropdownMenuItem(text={Text(if(value)"时间（早到晚）" else "时间（晚到早）")},onClick={ascending=value;sortMenu=false})}}}
                    Box{NotificationFilter("应用筛选",page["applications"].asRows().firstOrNull{it.text("packageName")==packageName}?.let{"${it.text("appName")}（${it.number("count")}）"}?:"全部应用"){appsMenu=true};DropdownMenu(appsMenu,{appsMenu=false},modifier=Modifier.hingeWindowTapFeedback()){DropdownMenuItem(text={Text("全部应用")},onClick={packageName="";appsMenu=false});page["applications"].asRows().forEach{app->DropdownMenuItem(text={Text("${app.text("appName")}（${app.number("count")}）")},onClick={packageName=app.text("packageName");appsMenu=false})}}}
                }};Spacer(Modifier.height(12.dp))}
                if(failure.isNotEmpty())item{OriginalCard(color=MaterialTheme.colorScheme.errorContainer){OriginalRow("读取通知历史失败",failure,"Symbols.error_outline_rounded",iconColor=MaterialTheme.colorScheme.onErrorContainer,titleStyle=MaterialTheme.typography.bodyLarge.copy(color=MaterialTheme.colorScheme.onErrorContainer),subtitleStyle=MaterialTheme.typography.bodyMedium.copy(color=MaterialTheme.colorScheme.onErrorContainer))}}
                if(rows.isEmpty()&&!loading&&failure.isEmpty())item{OriginalCard{Column(Modifier.fillMaxWidth().padding(28.dp),horizontalAlignment=Alignment.CenterHorizontally){HIcon("Symbols.notifications_none_rounded",size=42.dp,color=MaterialTheme.colorScheme.primary);Spacer(Modifier.height(10.dp));Text("暂时没有符合条件的通知")}}}
                items(rows,key={it.text("id")}){row->
                    fun open(){model.run{if(model.api.call("openNotificationHistoryItem",row)!=true)model.message="无法打开“${row.text("appName")}”，应用可能已被卸载"}}
                    OriginalCard(Modifier.clickable{open()}){Row(Modifier.heightIn(min=100.dp).padding(horizontal=14.dp,vertical=14.dp),verticalAlignment=Alignment.CenterVertically){
                        EncodedAppIcon(row.text("iconBase64"),true);Spacer(Modifier.width(16.dp));Column(Modifier.weight(1f)){
                            Text(row.text("appName")+row.text("title").let{if(it.isEmpty())"" else " · $it"},maxLines=1,overflow=TextOverflow.Ellipsis,style=MaterialTheme.typography.bodyLarge)
                            Spacer(Modifier.height(4.dp));Text("${row.text("content").ifEmpty{"（无正文）"}}\n${if(row.number("timestamp")<=0)"时间未知" else SimpleDateFormat("yyyy/M/d HH:mm",locale).format(Date(row.number("timestamp")))}",maxLines=5,overflow=TextOverflow.Ellipsis,style=MaterialTheme.typography.bodyMedium)
                        }
                        Spacer(Modifier.width(16.dp))
                        if(row["isVerificationCode"]==true&&row.text("verificationCode").isNotEmpty())IconButton(onClick={copyText(model,row.text("verificationCode"))}){HIcon("Symbols.content_copy_rounded","复制验证码",color=MaterialTheme.colorScheme.primary)}
                        if(row.text("packageName") in setOf("com.tencent.mm","com.tencent.mobileqq"))IconButton(onClick={open()}){HIcon("Symbols.open_in_new_rounded","打开应用",color=MaterialTheme.colorScheme.primary)}
                        IconButton(onClick={model.run{model.api.call("deleteNotificationHistoryItem",mapOf("id" to row.text("id")));revision++}}){HIcon("Symbols.delete_rounded","删除通知")}
                    }};Spacer(Modifier.height(10.dp))
                }
            }
            if(loading)item{Box(Modifier.fillMaxWidth().padding(20.dp),contentAlignment=Alignment.Center){HingeCircularProgressIndicator()}}
        }
    }
    if(clear)HingeAlertDialog(onDismissRequest={clear=false},title={Text("清空通知历史？")},text={Text("所有已保存的通知记录都会被删除，且无法恢复。")},dismissButton={TextButton(onClick={clear=false}){Text("取消")}},confirmButton={HingeFilledButton(onClick={model.run{val count=model.api.call("clearNotificationHistory");model.message="已清空 $count 条通知";clear=false;revision++}}){Text("清空")}})
}

@Composable private fun NotificationFilter(label:String,value:String,open:()->Unit){
    Surface(Modifier.fillMaxWidth().clickable(onClick=open),shape=RoundedCornerShape(16.dp),color=MaterialTheme.colorScheme.surfaceContainerHighest){
        Row(Modifier.heightIn(min=64.dp).padding(horizontal=12.dp,vertical=8.dp),verticalAlignment=Alignment.CenterVertically){
            Column(Modifier.weight(1f)){Text(label,style=MaterialTheme.typography.bodySmall,color=MaterialTheme.colorScheme.onSurfaceVariant);Text(value,style=MaterialTheme.typography.bodyLarge,maxLines=1,overflow=TextOverflow.Ellipsis)}
            HIcon("Icons.arrow_drop_down",color=MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

fun historyStatus(row:Map<String,Any?>):String{
    val send=row.text("direction")=="send";val error=row.text("error")
    return when(row.text("status")){
        "queued"->if(error.isNotEmpty())"等待重试 · $error" else if(send)"等待发送" else "等待接收"
        "transferring"->(if(send)"正在发送" else "正在接收")+if(row.number("totalBytes")>0)" · ${(row.number("bytesTransferred")*100/row.number("totalBytes")).coerceIn(0,100)}%" else ""
        "awaitingPickup"->"已上传 Cloud Relay · 等待设备接收"
        "completed"->if(send)"发送完成" else "接收完成"
        "failed"->if(error.isEmpty())"传输失败" else "失败 · $error"
        "cancelled"->"已取消"
        else->row.text("status")
    }
}
fun formatBytes(value:Long)=when{value<1024->"$value B";value<1024*1024->"%.1f KB".format(value/1024.0);value<1024*1024*1024->"%.1f MB".format(value/1048576.0);else->"%.2f GB".format(value/1073741824.0)}
@Composable fun OriginalHistory(model:HingeModel,back:()->Unit){
    var clear by remember{mutableStateOf(false)}
    Scaffold(topBar={TopAppBar(expandedHeight = 56.dp, title={Text("文件传输记录",style=MaterialTheme.typography.titleLarge)},navigationIcon={IconButton(onClick=back){HIcon("Icons.arrow_back","返回")}},actions={IconButton(onClick={clear=true},enabled=model.history.isNotEmpty()){HIcon("Icons.delete_sweep_outlined","清空本机记录")}})}){padding->
        if(model.history.isEmpty())Box(Modifier.fillMaxSize().padding(padding).padding(32.dp),contentAlignment=Alignment.Center){Column(horizontalAlignment=Alignment.CenterHorizontally){HIcon("Icons.history_rounded",size=56.dp,color=MaterialTheme.colorScheme.onSurfaceVariant);Spacer(Modifier.height(12.dp));Text("暂无文件传输记录",style=MaterialTheme.typography.titleMedium);Spacer(Modifier.height(6.dp));Text("通过局域网或 Cloud Relay 收发文件后，记录会显示在这里。",textAlign=TextAlign.Center,color=MaterialTheme.colorScheme.onSurfaceVariant)}}
        else LazyColumn(Modifier.padding(padding),contentPadding=PaddingValues(start=16.dp,top=12.dp,end=16.dp,bottom=24.dp),verticalArrangement=Arrangement.spacedBy(8.dp)){
            items(model.history,key={it.text("id")}){row->
                val status=row.text("status");val active=status in setOf("queued","transferring");val canOpen=row.text("localFilePath").isNotEmpty()||(row.text("direction")=="receive"&&status=="completed")
                OriginalCard(Modifier.then(if(canOpen)Modifier.clickable{model.run{val path=row.text("localFilePath");if(path.isEmpty())model.message="这是旧记录，未保存本机文件位置，无法直接打开。" else when(model.api.call("openTransferHistoryFile",mapOf("path" to path))){"opened"->Unit;"missing"->model.message="文件已不存在或已被删除。";else->model.message="无法打开文件，请检查“默认应用”设置或文件访问权限。"}}}else Modifier)){
                    Column(Modifier.padding(16.dp)){
                        Row(verticalAlignment=Alignment.CenterVertically){HIcon(if(row.text("direction")=="send")"Icons.upload_rounded" else "Icons.download_rounded",color=MaterialTheme.colorScheme.primary);Spacer(Modifier.width(10.dp));Text(row.text("fileName"),Modifier.weight(1f),maxLines=2,overflow=TextOverflow.Ellipsis,style=MaterialTheme.typography.titleMedium);if(canOpen)HIcon("Icons.open_in_new_rounded",size=18.dp,color=MaterialTheme.colorScheme.onSurfaceVariant);if(!active)IconButton(onClick={model.deleteHistory(row.text("id"))},modifier=Modifier.size(40.dp)){HIcon("Icons.delete_outline_rounded","删除记录")}}
                        Spacer(Modifier.height(4.dp));val time=runCatching{Instant.parse(row.text("updatedAt")).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("MM-dd HH:mm"))}.getOrDefault("")
                        Text(listOf(if(row.text("direction")=="send")"发送" else "接收",row.text("deviceName"),historyStatus(row),time).filter{it.isNotEmpty()}.joinToString(" · "),style=MaterialTheme.typography.bodySmall,color=if(status=="failed")MaterialTheme.colorScheme.error else if(active)MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant)
                        if(status=="transferring"&&row.number("totalBytes")>0){Spacer(Modifier.height(12.dp));HingeLinearProgressIndicator(progress={(row.number("bytesTransferred").toFloat()/row.number("totalBytes")).coerceIn(0f,1f)},modifier=Modifier.fillMaxWidth());Spacer(Modifier.height(4.dp));Text("${formatBytes(row.number("bytesTransferred"))} / ${formatBytes(row.number("totalBytes"))}",Modifier.align(Alignment.End),style=MaterialTheme.typography.labelSmall)}
                    }
                }
            }
        }
    }
    if(clear)HingeAlertDialog(onDismissRequest={clear=false},title={Text("清空传输记录？")},text={Text("将从本机列表中移除 ${model.history.size} 条记录。此操作不会取消正在进行的传输，也不会删除已接收的文件。")},dismissButton={TextButton(onClick={clear=false}){Text("取消")}},confirmButton={HingeFilledButton(onClick={model.clearHistory();clear=false}){Text("清空记录")}})
}
