package com.hinge.office

import android.graphics.BitmapFactory
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.DialogProperties
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.window.Dialog

@Composable fun StorageDialog(model:HingeModel,dismiss:()->Unit){
    var images by remember{mutableStateOf(model.settings.text("imageStoragePath"))};var videos by remember{mutableStateOf(model.settings.text("videoStoragePath"))};var files by remember{mutableStateOf(model.settings.text("fileStoragePath"))}
    HingeAlertDialog(onDismissRequest=dismiss,title={Text("存储设置")},text={Column(Modifier.widthIn(max=560.dp).verticalScroll(rememberScrollState())){
        Text("安卓端默认写入 Download/Hinge，并按类型分类保存。");Spacer(Modifier.height(14.dp))
        HingeTextField(images,{images=it},label={Text("图片存储路径")},leadingIcon={HIcon("Symbols.image_rounded")});Spacer(Modifier.height(10.dp))
        HingeTextField(videos,{videos=it},label={Text("视频存储路径")},leadingIcon={HIcon("Symbols.video_library_rounded")});Spacer(Modifier.height(10.dp))
        HingeTextField(files,{files=it},label={Text("其他文件存储路径")},leadingIcon={HIcon("Symbols.folder_rounded")})
    }},dismissButton={TextButton(onClick=dismiss){Text("取消")}},confirmButton={HingeFilledButton(onClick={model.run{
        model.saveStoragePaths(images,videos,files);model.message="存储路径已更新";dismiss()
    }}){Text("保存")}})
}

@Composable fun AboutDialog(model:HingeModel,dismiss:()->Unit){
    val logo=remember{model.activity.assets.open("icon9.png").use{BitmapFactory.decodeStream(it)}}
    Dialog(onDismissRequest=dismiss,properties=DialogProperties(usePlatformDefaultWidth=false)){Surface(Modifier.padding(horizontal=28.dp,vertical=24.dp).widthIn(max=430.dp).hingeWindowTapFeedback(),shape=RoundedCornerShape(28.dp),color=MaterialTheme.colorScheme.surfaceContainerHigh){Column(Modifier.padding(start=24.dp,top=28.dp,end=24.dp,bottom=20.dp).verticalScroll(rememberScrollState()),horizontalAlignment=Alignment.CenterHorizontally){
        Box(Modifier.background(MaterialTheme.colorScheme.surfaceContainerHighest,RoundedCornerShape(24.dp)).padding(14.dp)){Image(logo.asImageBitmap(),"Hinge",Modifier.size(88.dp).clip(RoundedCornerShape(22.dp)))}
        Spacer(Modifier.height(18.dp));Row(verticalAlignment=Alignment.CenterVertically){Text("Hinge",style=MaterialTheme.typography.headlineSmall,fontWeight=FontWeight.W700);Spacer(Modifier.width(10.dp));Text("v${BuildConfig.VERSION_NAME}",Modifier.background(MaterialTheme.colorScheme.primaryContainer,RoundedCornerShape(999.dp)).padding(horizontal=11.dp,vertical=5.dp),fontWeight=FontWeight.W700,color=MaterialTheme.colorScheme.onPrimaryContainer)}
        Spacer(Modifier.height(8.dp));Text("局域网优先的跨设备办公工作台",style=MaterialTheme.typography.bodyLarge,textAlign=TextAlign.Center);Spacer(Modifier.height(20.dp))
        listOf("devices" to "设备发现、连接与文件访问","workspaces" to "笔记、待办、日历与相册工作区","sync" to "数据默认保存在设备本地").forEach{(icon,label)->Row(Modifier.fillMaxWidth().padding(vertical=7.dp)){HIcon("Symbols.${icon}_rounded",size=22.dp,color=MaterialTheme.colorScheme.primary);Spacer(Modifier.width(14.dp));Text(label,Modifier.weight(1f))}}
        Spacer(Modifier.height(12.dp))
        Surface(
            shape = RoundedCornerShape(18.dp),
            color = MaterialTheme.colorScheme.surfaceContainerHighest,
            contentColor = MaterialTheme.colorScheme.onSurface,
        ) {
            OriginalRow(
                title = "GitHub 仓库",
                icon = "Symbols.code_rounded",
                iconColor = MaterialTheme.colorScheme.primary,
                action = { model.run { if (model.api.call("openProjectUrl") != true) model.message = "无法打开 GitHub 项目地址" } },
                trailing = { HIcon("Symbols.open_in_new_rounded") },
                endPadding = 16.dp,
            )
        }
        Spacer(Modifier.height(20.dp));HingeFilledButton(onClick=dismiss,modifier=Modifier.fillMaxWidth()){Text("我知道了")}
    }}}
}

@Composable fun OriginalScreenControl(model:HingeModel,open:()->Unit){
    var status by remember{mutableStateOf<Map<String,Any?>>(emptyMap())};var opening by remember{mutableStateOf(false)}
    LaunchedEffect(model.foreground){model.run{status=model.api.call("screenControlStatus").asMap()}}
    OriginalPage{
        OriginalCard{OriginalRow("手机控制电脑","电脑桌面、触控板与游戏模式","Icons.computer_outlined",action=open)};Spacer(Modifier.height(16.dp))
        OriginalCard{Column(Modifier.padding(16.dp)){
            Text("电脑控制手机",style=MaterialTheme.typography.titleMedium);Spacer(Modifier.height(8.dp));Text(when{status["supported"]==false->"需要 Android 11 及以上系统。";status["developerOptionsEnabled"]==false->"请先开启开发者选项和无线调试。";status["developerOptionsEnabled"]==true->"开发者选项已开启，请在系统设置确认无线调试状态。";else->"请在系统设置中开启无线调试。"})
            Spacer(Modifier.height(16.dp));HingeFilledButton(onClick={opening=true;model.run{try{if(model.api.call("openWirelessDebuggingSettings")!=true)model.message="无法打开系统设置，请手动进入开发者选项中的无线调试。"}finally{opening=false}}},enabled=status["supported"]!=false&&!opening,modifier=Modifier.fillMaxWidth()){HIcon("Icons.settings_outlined");Spacer(Modifier.width(8.dp));Text(if(opening)"正在打开…" else "打开系统调试设置")}
        }};Spacer(Modifier.height(24.dp));Text("连接说明",style=MaterialTheme.typography.titleMedium);Spacer(Modifier.height(8.dp));Text("手机与电脑连接同一网络。开启无线调试后，在电脑的“投屏与远控”中完成配对并开始投屏。仅向可信电脑授权。")
    }
}

@Composable fun OriginalPermissionPrompts(model:HingeModel,openSms:()->Unit){
    var checked by rememberSaveable{mutableStateOf(false)}
    var prompt by rememberSaveable{mutableStateOf("")}
    val permissionCalls=listOf("hasCalendarPermission" to "requestCalendarPermission","hasPhotosPermission" to "requestPhotosPermission","hasMediaPermission" to "requestMediaPermission","hasAllFilesAccess" to "openAllFilesAccessSettings","hasNotificationPermission" to "requestNotificationPermission")
    suspend fun smsPrompt(){
        if(model.api.call("smsPermissionPromptShown")==true)return
        val permissions=model.api.call("smsPermissionStatus").asMap()
        model.api.call("setSmsPermissionPromptShown",mapOf("shown" to true))
        if(permissions["sms"]!=true||permissions["readSms"]!=true)prompt="sms"
    }
    LaunchedEffect(model.initialized){if(model.initialized&&!checked){checked=true;model.run{var missing=false;for((has,_)in permissionCalls)if(model.api.call(has)!=true)missing=true;if(missing)prompt="workspace" else smsPrompt()}}}
    if(prompt=="workspace")HingeAlertDialog(onDismissRequest={prompt="";model.run{smsPrompt()}},title={Text("完善手机工作区")},text={Text("为了让 Windows 端完整查看手机日历、照片、视频、音频、PDF、DOC、XLS、PPT 和 TXT 文件，并在收到文件时及时提醒，Hinge 需要日历、媒体、通知以及系统“所有文件访问”权限。笔记和待办保存在本机，不需要额外权限。")},dismissButton={TextButton(onClick={prompt="";model.run{smsPrompt()}}){Text("稍后处理")}},confirmButton={HingeFilledButton(onClick={prompt="";model.run{for((has,request)in permissionCalls)if(model.api.call(has)!=true)model.api.call(request);smsPrompt()}}){Text("继续授权")}})
    if(prompt=="sms")HingeAlertDialog(onDismissRequest={prompt=""},title={Text("开启短信同步")},text={Text("如果你希望在 Windows 上收到手机短信和验证码提醒，需要额外允许 Hinge 接收短信，并在系统允许时开启“访问短信/彩信”。这项功能默认关闭，不会读取历史短信。")},dismissButton={TextButton(onClick={prompt=""}){Text("稍后处理")}},confirmButton={HingeFilledButton(onClick={prompt="";openSms()}){Text("去授权")}})
}
