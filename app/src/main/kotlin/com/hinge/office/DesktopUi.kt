@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
package com.hinge.office

import android.content.pm.ActivityInfo
import android.view.MotionEvent
import android.view.View
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import kotlinx.coroutines.delay
import org.json.JSONObject
import kotlin.math.*

@Composable fun DesktopPage(model: HingeModel, mode: String, active: Boolean, exit: () -> Unit) {
    val peer = model.selectedPeer.takeIf { it.isNotEmpty() } ?: model.ready.firstOrNull()
    if (peer == null) { ContentColumn { Text("请先连接 Windows 电脑。"); HingeFilledButton(onClick=exit){Text("返回") } }; return }
    val desktopMode = mode == "电脑桌面"
    val remote = remember(peer.text("connectionId"), desktopMode,model.remoteRevision) { RemoteController(model, peer, desktopMode) }
    var sensitivityDialog by remember{mutableStateOf(false)}
    var gameDocument by remember { mutableStateOf<PresetDocument?>(null) }
    val options=remember { DesktopOptions().apply { mapping=mode=="游戏模式";gameHaptics=model.settings["hapticFeedbackEnabled"]!=false } }
    val landscape = LocalConfiguration.current.orientation == android.content.res.Configuration.ORIENTATION_LANDSCAPE
    val railWidth=(LocalConfiguration.current.screenWidthDp*.07f).coerceIn(64f,72f).dp
    val density=androidx.compose.ui.platform.LocalDensity.current
    val cutouts=WindowInsets.displayCutout
    val railExtent=railWidth+with(density){maxOf(cutouts.getLeft(density,androidx.compose.ui.unit.LayoutDirection.Ltr),cutouts.getRight(density,androidx.compose.ui.unit.LayoutDirection.Ltr)).toDp()}
    val fullDesktop = desktopMode && (options.full || landscape || options.mapping)
    val showMappingControls = options.mapping && !options.zoomMode
    val showDesktopRails = fullDesktop && !showMappingControls
    LaunchedEffect(Unit) {
        gameDocument=loadGameDocument(model,desktopMode)
        if(desktopMode) options.direct=model.api.call("loadInputMode",mapOf("peerId" to "desktopInputMode.${peer.text("deviceId")}"),"hinge/desktop_video")==true
    }
    DisposableEffect(remote) {
        model.remote=remote
        if(!desktopMode) remote.start()
        onDispose { remote.close(); if(model.remote===remote) model.remote=null; model.activity.requestedOrientation=ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED }
    }
    LaunchedEffect(model.desktop["sessionId"]){options.systemPending=false}
    LaunchedEffect(remote){if(desktopMode){while(!model.activity.desktopSurface.isAvailable)delay(30);remote.start()}}
    LaunchedEffect(model.presetRevision,options.editing){if(!options.editing){remote.releaseKeys();gameDocument=loadGameDocument(model,desktopMode)}}
    LaunchedEffect(options.actions,options.editing,options.presetPicker,options.gamePreferences,options.viewOnly,options.zoomMode){remote.releaseKeys()}
    DisposableEffect(fullDesktop,options.mapping){
        val controller=androidx.core.view.WindowCompat.getInsetsController(model.activity.window,model.activity.window.decorView)
        if(fullDesktop||options.mapping){controller.hide(androidx.core.view.WindowInsetsCompat.Type.systemBars());controller.systemBarsBehavior=androidx.core.view.WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE}
        else controller.show(androidx.core.view.WindowInsetsCompat.Type.systemBars())
        onDispose{controller.show(androidx.core.view.WindowInsetsCompat.Type.systemBars())}
    }
    SideEffect{remote.allowRecovery=!options.systemPending}
    val statistics=desktopStatistics(model,desktopMode&&options.showStats)
    val canInput = remote.ready && !options.viewOnly && !options.systemPending && !options.zoomMode
    val canAdjustView = remote.ready && !options.systemPending
    val zoomLabel = if (options.zoomMode) "鼠标操作" else "缩放画面"
    val zoomIcon = if (options.zoomMode) "mouse" else "zoom_in"
    fun toggleZoomMode() {
        if (!canAdjustView) return
        remote.releaseKeys()
        options.zoomMode = !options.zoomMode
    }
    fun setDesktopFullscreen(full: Boolean) {
        remote.releaseKeys()
        options.full = full
        if (full) options.zoomMode = false else options.mapping = false
        model.activity.requestedOrientation = if (full) ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
            else ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT
    }
    BackHandler(enabled = active) {
        when {
            fullDesktop -> setDesktopFullscreen(false)
            options.mapping -> options.mapping = false
            else -> exit()
        }
    }
    fun shortcut(key:Int) { if(!canInput)return;remote.key(91,true);remote.key(key,true);remote.key(key,false);remote.key(91,false) }
    @Composable fun icon(name:String,label:String,enabled:Boolean=true,click:()->Unit) { IconButton(onClick=click,enabled=enabled){ HIcon("Icons.$name",label) } }
    @Composable fun rail(left:Boolean) {
        Box(Modifier.width(railExtent).fillMaxHeight().background(Color(0xff050505)).windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Vertical + if(left)WindowInsetsSides.Left else WindowInsetsSides.Right))){
        Column(Modifier.width(railWidth).fillMaxHeight().align(if(left)Alignment.TopStart else Alignment.TopEnd).verticalScroll(rememberScrollState()),horizontalAlignment=Alignment.CenterHorizontally) {
            @Composable fun button(label:String,name:String,enabled:Boolean=true,click:()->Unit) {
                val color=Color.White.copy(alpha=if(enabled)1f else .38f)
                Column(Modifier.fillMaxWidth().clickable(enabled=enabled,onClick=click).heightIn(min=64.dp).padding(horizontal=3.dp,vertical=8.dp),horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.Center){HIcon("Icons.$name",size=22.dp,color=color);Spacer(Modifier.height(3.dp));Text(label,color=color,fontSize=11.sp,lineHeight=12.1.sp,textAlign=TextAlign.Center,maxLines=2,overflow=androidx.compose.ui.text.style.TextOverflow.Ellipsis)}
            }
            if (left) {
                if (options.mouseButtons) button("右键", "mouse_outlined", canInput) {
                    if (canInput) remote.input(Wire.input(4, 1))
                }
                button(zoomLabel, zoomIcon, canAdjustView, ::toggleZoomMode)
                button("显示桌面", "desktop_windows_outlined", canInput) { shortcut(68) }
                button("所有窗口", "grid_view", canInput) { shortcut(9) }
            } else {
                button("操作","tune",model.desktop.text("state") in listOf("ready","stats","format")){options.panelPage="操作";options.actions=true}
                button("显示","monitor",model.desktop.text("state") in listOf("ready","stats","format")){options.panelPage="显示";options.actions=true}
                button("键盘","keyboard",canInput){options.textInput=true}
                button("退出全屏","fullscreen_exit"){setDesktopFullscreen(false)}
            }
        }
        }
    }
    @Composable fun surface(modifier:Modifier) {
        Box(modifier.then(if(fullDesktop)Modifier else Modifier.border(1.dp,MaterialTheme.colorScheme.outlineVariant,RoundedCornerShape(20.dp)).clip(androidx.compose.foundation.shape.RoundedCornerShape(20.dp))).clipToBounds().background(if(desktopMode) Color.Black else MaterialTheme.colorScheme.surfaceContainerHighest.copy(alpha=.55f))) {
            if(desktopMode) {
                val width = model.desktop.number("width",16).toFloat();val height=model.desktop.number("height",9).toFloat()
                BoxWithConstraints(Modifier.fillMaxSize(),contentAlignment=Alignment.Center) {
                    val videoWidth=minOf(maxWidth.value,maxHeight.value*width/height)
                    AndroidView(factory={model.activity.desktopSurface},modifier=Modifier.size(videoWidth.dp,(videoWidth*height/width).dp).graphicsLayer{scaleX=options.zoom;scaleY=options.zoom;translationX=options.panX;translationY=options.panY})
                }
            } else if(landscape)Row(Modifier.align(Alignment.BottomCenter).padding(bottom=12.dp)){listOf("左键","右键").forEach{Text(it,Modifier.weight(1f),textAlign=TextAlign.Center,style=MaterialTheme.typography.labelMedium)}}
            else Box(Modifier.fillMaxSize(),contentAlignment=Alignment.Center) {
                Text("单指移动 · 轻点左键\n双指轻点右键 · 双指滑动滚动\n双击后按住并移动以拖拽",textAlign=TextAlign.Center,color=MaterialTheme.colorScheme.onSurfaceVariant)
            }
            AndroidView(
                factory = { GestureSurface(model.activity, remote) },
                modifier = Modifier.fillMaxSize(),
                update = {
                    it.sensitivity = options.sensitivity
                    it.isHapticFeedbackEnabled = model.settings["hapticFeedbackEnabled"] != false
                    it.desktop = desktopMode
                    it.panOnly = options.zoomMode
                    it.direct = options.direct && desktopMode && model.desktop["absoluteMouse"] == true
                    it.inputEnabled = (canInput || (options.zoomMode && canAdjustView)) &&
                        !options.actions && !options.editing && !options.presetPicker && !options.gamePreferences
                    it.aspect = model.desktop.number("width", 16).toFloat() / model.desktop.number("height", 9)
                    it.zoom = options.zoom
                    it.panX = options.panX
                    it.panY = options.panY
                    it.onViewChanged = { scale, x, y -> options.zoom = scale; options.panX = x; options.panY = y }
                },
            )
            if(desktopMode&&model.desktop.text("state") !in listOf("ready","stats","format"))Column(Modifier.align(Alignment.Center).padding(20.dp),horizontalAlignment=Alignment.CenterHorizontally){
                Text(remote.status,color=Color.White,textAlign=TextAlign.Center)
                Spacer(Modifier.height(12.dp));TextButton(enabled=model.desktop.text("state") in listOf("error","stopped")||remote.isClosed,onClick={model.remoteRevision++}){Text("重新连接")}
            }
            if(desktopMode&&model.desktop.text("state") in listOf("ready","stats","format")){
                val inputLabel=if(options.zoomMode)"画面缩放 / 平移" else if(options.viewOnly)"仅观看" else if(options.mapping)"按键映射 · ${if(options.gameCamera)"游戏视角" else if(options.direct&&model.desktop["absoluteMouse"]==true)"直接触控" else "鼠标指针"}" else if(options.direct&&model.desktop["absoluteMouse"]==true)"直接触控" else "鼠标指针"
                Text(inputLabel,Modifier.align(Alignment.BottomStart).padding(start=12.dp,bottom=12.dp).background(Color.Black.copy(alpha=.54f),RoundedCornerShape(12.dp)).padding(horizontal=10.dp,vertical=6.dp),color=Color.White,style=MaterialTheme.typography.bodyMedium.flutterFontSize(12f))
                if(options.showStats)Text(statistics.ifEmpty{remote.status},Modifier.align(Alignment.TopStart).padding(8.dp).fillMaxWidth().background(Color.Black.copy(alpha=.54f),RoundedCornerShape(8.dp)).padding(6.dp),color=Color.White,style=MaterialTheme.typography.bodyMedium.flutterFontSize(11f),maxLines=4,overflow=androidx.compose.ui.text.style.TextOverflow.Ellipsis)
            }
        }
    }
    val currentPreset=gameDocument?.let{d->d.presets.firstOrNull{it.id==d.selected}?:d.presets.first()}
    Box(Modifier.fillMaxSize()
        .then(if (options.actions) Modifier.clearAndSetSemantics {} else Modifier)
        .then(if(fullDesktop)Modifier else Modifier.windowInsetsPadding(WindowInsets.displayCutout))) {
        if(fullDesktop) {
            Row(Modifier.fillMaxSize().background(Color.Black)) {
                if (showDesktopRails) rail(true) else Spacer(Modifier.width(railExtent))
                surface(Modifier.weight(1f).fillMaxHeight())
                if (showDesktopRails) rail(false) else Spacer(Modifier.width(railExtent))
            }
        } else if(!desktopMode&&landscape&&!options.mapping)Column(Modifier.fillMaxSize().systemBarsPadding()){
            Column(Modifier.weight(1f).verticalScroll(rememberScrollState())){
                Row(verticalAlignment=Alignment.CenterVertically){icon("arrow_back","返回",click=exit);Text(remote.status,Modifier.weight(1f),maxLines=1,style=MaterialTheme.typography.titleSmall)
                    icon("sports_esports","游戏模式",enabled=canInput&&remote.supportsGameKeys){if(remote.ready&&remote.supportsGameKeys)options.mapping=true}
                    icon("speed","调节灵敏度"){sensitivityDialog=true};icon("keyboard","发送文本",enabled=canInput){options.textInput=true};icon("screen_rotation","切换竖屏"){model.activity.requestedOrientation=ActivityInfo.SCREEN_ORIENTATION_PORTRAIT}}
                Text("左侧轻点：左键 · 右侧轻点：右键 · 单指滑动：移动鼠标 · 双指滑动：滚动 · 左侧双击按住：拖拽",Modifier.padding(horizontal=16.dp),style=MaterialTheme.typography.bodySmall)
            }
            surface(Modifier.weight(3f).fillMaxWidth().padding(start=16.dp,top=4.dp,end=16.dp,bottom=8.dp))
        } else Column(Modifier.fillMaxSize().navigationBarsPadding()) {
            TopAppBar(expandedHeight = 56.dp, title={Text("控制 ${peer.text("name")}",style=MaterialTheme.typography.titleLarge)},navigationIcon={icon("arrow_back","返回",click=exit)},actions={
                if(desktopMode) { icon("tune","桌面操作"){options.panelPage="操作";options.actions=true};icon(zoomIcon,zoomLabel,enabled=canAdjustView,click=::toggleZoomMode);icon("fullscreen","全屏显示桌面"){setDesktopFullscreen(true)} }
                else { icon("screen_rotation","切换横屏"){model.activity.requestedOrientation=if(landscape)ActivityInfo.SCREEN_ORIENTATION_PORTRAIT else ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE};icon("sports_esports","游戏模式",enabled=canInput&&remote.supportsGameKeys){if(remote.supportsGameKeys){options.mapping=true;model.activity.requestedOrientation=ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE}} }
                icon("keyboard","虚拟键盘输入",enabled=canInput){if(desktopMode){options.panelPage="键盘";options.actions=true}else options.textInput=true}
            })
            Row(Modifier.fillMaxWidth().heightIn(min=48.dp).padding(horizontal=16.dp,vertical=8.dp),verticalAlignment=Alignment.CenterVertically){HIcon(if(remote.ready)"Icons.check_circle" else "Icons.info_outline",color=if(remote.ready)MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant);Spacer(Modifier.width(16.dp));Text(remote.status,style=MaterialTheme.typography.bodyMedium)}
            if(!desktopMode) Row(Modifier.padding(horizontal=16.dp),verticalAlignment=Alignment.CenterVertically){ HIcon("Icons.speed",size=18.dp);Spacer(Modifier.width(8.dp));Text("灵敏度 ${"%.1f".format(options.sensitivity)}×",style=MaterialTheme.typography.bodySmall);HingeSlider(options.sensitivity,{options.sensitivity=it},valueRange=1.5f..5f,steps=13,enabled=canInput,modifier=Modifier.weight(1f)) }
            surface(Modifier.weight(1f).fillMaxWidth().padding(start=16.dp,top=4.dp,end=16.dp,bottom=8.dp))
            if(!desktopMode||options.mouseButtons) Row(Modifier.padding(start=16.dp,end=16.dp,bottom=12.dp,top=4.dp),horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                HingeTonalButton(onClick={remote.input(Wire.input(4))},enabled=canInput,modifier=Modifier.weight(1f)){Text("左键")}
                FilledTonalIconButton(onClick={options.textInput=true},enabled=canInput){HIcon("Icons.keyboard","发送文本")}
                HingeTonalButton(onClick={remote.input(Wire.input(4,1))},enabled=canInput,modifier=Modifier.weight(1f)){Text("右键")}
            }
            Text(if(desktopMode) "普通滑动只移动鼠标；停住长按后再移动才拖拽，抬手松开。双指轻点右键、滑动滚动。切到缩放画面后可双指捏合或拖动画面。" else "此功能传输控制输入，不显示电脑画面。${if(remote.encrypted)"当前控制流已加密。" else "当前局域网控制流未加密，请勿输入密码或敏感内容。"}",modifier=Modifier.padding(start=20.dp,end=20.dp,bottom=8.dp),style=MaterialTheme.typography.bodySmall,textAlign=TextAlign.Center)
        }
        if (showMappingControls && currentPreset != null) {
            Box(Modifier.fillMaxSize().then(if(fullDesktop)Modifier.windowInsetsPadding(WindowInsets.displayCutout)else Modifier)){
                GameOverlay(model,remote,currentPreset,desktopMode,options)
                Row(Modifier.align(Alignment.TopCenter).padding(top=2.dp)){FilledTonalIconButton(onClick={options.mapping=false}){HIcon("Icons.close","退出按键映射")};if(desktopMode){Spacer(Modifier.width(8.dp));FilledTonalIconButton(onClick={remote.releaseKeys();options.actions=true}){HIcon("Icons.settings_outlined","按键映射设置")}}}
                if(!desktopMode)FilledTonalIconButton(onClick={remote.releaseKeys();options.presetPicker=true},modifier=Modifier.align(Alignment.TopEnd).padding(top=2.dp,end=8.dp)){HIcon("Icons.settings_outlined","游戏预设")}
                if(!remote.ready||!remote.supportsGameKeys)Surface(Modifier.align(Alignment.BottomCenter).padding(start=16.dp,end=16.dp,bottom=8.dp),shape=RoundedCornerShape(12.dp),color=MaterialTheme.colorScheme.errorContainer){Text("控制已停止，所有按键已释放。请返回触控板重新连接。",Modifier.padding(12.dp),color=MaterialTheme.colorScheme.onErrorContainer)}
            }
        }
    }
    if(options.editing && currentPreset!=null) {
        remote.releaseKeys()
        PresetEditor(model,currentPreset,{options.editing=false}) { preset -> model.run {
            val doc=loadGameDocument(model,desktopMode);require(doc.presets.any{it.id==preset.id}){"该预设已在其他设备删除，请重新打开布局"};val next=doc.copy(presets=doc.presets.map{if(it.id==preset.id)preset else it})
            saveGameDocument(model,next,desktopMode);gameDocument=next;options.editing=false
        } }
    }
    if(model.desktop.text("state")=="verify" && desktopMode) HingeAlertDialog(onDismissRequest={remote.close();exit()},dismissOnClickOutside=false,title={Text("核对电脑桌面身份")},text={Text("请确认电脑也显示完全相同的身份码：\n\n${model.desktop.text("code")}\n\n两端一致后分别允许，后续无需重复确认。")},confirmButton={HingeFilledButton(onClick={model.run{model.api.call("confirm",mapOf("sessionId" to model.desktop.number("sessionId"),"allow" to true),"hinge/desktop_video");model.desktop=model.desktop+("state" to "connecting")}}){Text("身份码一致")}},dismissButton={TextButton(onClick={remote.close();exit()}){Text("拒绝")}})
    if(remote.needsKey) {
        var key by remember{mutableStateOf("")};var rememberKey by remember{mutableStateOf(true)}
        HingeAlertDialog(onDismissRequest={remote.close();exit()},title={Text("加密远控密钥")},text={Column{HingeTextField(key,{key=it},label={Text("Windows 投屏与远控 → 手机远控电脑 → 密钥")},placeholder={Text("HRC1-…")},visualTransformation=PasswordVisualTransformation());Row{Checkbox(rememberKey,{rememberKey=it});Text("安全保存在此手机")};Text("使用 Android Keystore 加密保护\n仅在电脑启用远控加密时需要输入。")}},confirmButton={TextButton(onClick={model.run{require(key.trim().startsWith("HRC1-")){"请输入电脑端的远控密钥"};remote.acceptKey(java.util.Base64.getDecoder().decode(key.trim().removePrefix("HRC1-")),rememberKey)}}){Text("连接")}},dismissButton={TextButton(onClick={remote.close();exit()}){Text("取消")}})
    }
    if(sensitivityDialog)HingeAlertDialog(onDismissRequest={sensitivityDialog=false},title={Text("灵敏度 ${"%.1f".format(options.sensitivity)}×")},text={HingeSlider(options.sensitivity,{options.sensitivity=it},valueRange=1.5f..5f,steps=13)},confirmButton={TextButton(onClick={sensitivityDialog=false}){Text("完成")}})
    if(options.textInput) { var text by remember{mutableStateOf("")};HingeAlertDialog(onDismissRequest={options.textInput=false},title={Text("向电脑光标处发送文本")},text={HingeTextField(text,{text=it.take(4096)},autofocus=true,placeholder={Text("输入要发送到远程电脑光标处的文本…")},supportingText={Text("${text.length}/4096",Modifier.fillMaxWidth(),textAlign=TextAlign.End)})},confirmButton={HingeFilledButton(onClick={if(canInput&&text.isNotEmpty())remote.sendText(text);options.textInput=false}){HIcon("Symbols.send_rounded",size=16.dp);Spacer(Modifier.width(8.dp));Text("发送文本")}},dismissButton={TextButton(onClick={options.textInput=false}){Text("取消")}}) }
    if (desktopMode) DesktopTools(
        model, remote, options, currentPreset?.name ?: "桌面精简布局",
        active, fullDesktop, showDesktopRails, exit, toggleFull = { setDesktopFullscreen(!fullDesktop) },
        enableMapping = { enabled ->
            remote.releaseKeys()
            if (enabled && !fullDesktop) setDesktopFullscreen(true)
            options.mapping = enabled
            if (enabled) options.zoomMode = false
        },
    )
    if(options.presetPicker) ModalBottomSheet(dragHandle=null,onDismissRequest={options.presetPicker=false;model.run{gameDocument=loadGameDocument(model,desktopMode)}},sheetMaxWidth=if(desktopMode)420.dp else 640.dp) {
        Box(Modifier.fillMaxHeight(.9f).hingeWindowTapFeedback()){PresetsPage(model,desktopMode,edit={preset->options.presetPicker=false;model.run{gameDocument=loadGameDocument(model,desktopMode).copy(selected=preset.id);options.editing=true}},preferences=if(desktopMode)null else ({GamePreferences(options,false)}))}
    }
    if(options.gamePreferences) ModalBottomSheet(dragHandle=null,onDismissRequest={options.gamePreferences=false},sheetMaxWidth=if(desktopMode)420.dp else 640.dp) {
        val type=MaterialTheme.typography
        val typography=if(desktopMode)type.copy(titleLarge=type.titleLarge.flutterFontSize(18f),bodyLarge=type.bodyLarge.flutterFontSize(13f),bodyMedium=type.bodyMedium.flutterFontSize(13f),bodySmall=type.bodySmall.flutterFontSize(11f))else type
        MaterialTheme(typography=typography){ProvideTextStyle(typography.bodyMedium){Column(Modifier.fillMaxHeight(.9f).hingeWindowTapFeedback().verticalScroll(rememberScrollState()).padding(16.dp)){Text("映射设置",style=MaterialTheme.typography.titleLarge);GamePreferences(options,desktopMode)}}}
    }
}

@Composable private fun GamePreferences(options:DesktopOptions,desktop:Boolean){
    OriginalRow("触控反馈",minHeight=if(desktop)48.dp else null,trailing={Switch(options.gameHaptics,{options.gameHaptics=it})})
    Text("${if(desktop)"鼠标" else "视角"}灵敏度 ${"%.1f".format(options.sensitivity)}")
    HingeSlider(options.sensitivity,{options.sensitivity=it},valueRange=1.5f..5f,steps=13)
    if(desktop){Text("按键不透明度 ${(options.opacity*100).roundToInt()}%");HingeSlider(options.opacity,{options.opacity=it},valueRange=.2f.. .9f,steps=13)}
    Text(if(desktop)"沿用操作面板所选的触控或指针模式；按住映射的鼠标键滑动可拖拽。" else "此模式控制电脑键鼠；电脑画面仍需在电脑屏幕查看。部分游戏或管理员窗口可能不接受系统模拟输入。")
}

private class GestureSurface(context:android.content.Context,private val remote:RemoteController):View(context) {
    var sensitivity=3f
    var direct=false
        set(value){if(field!=value)resetDesktop();field=value}
    var inputEnabled=false
        set(value){if(field!=value)resetDesktop();field=value}
    var desktop=false;var zoom=1f;var aspect=16f/9
    var panOnly=false
        set(value){if(field!=value)resetDesktop();field=value}
    var panX = 0f
    var panY = 0f
    private var startX=0f;private var startY=0f;private var previousX=0f;private var previousY=0f
    private var moved=false;private var multi=false;private var dragging=false
    private val touchpad=TouchpadGesture({x,y->remote.input(Wire.input(1,x=x,y=y))},{remote.input(Wire.input(6,wheel=it))},{remote.input(Wire.input(4,it))},{remote.key(-1,it)})
    init{isClickable=true;contentDescription="电脑触控区域"}
    override fun performClick():Boolean {super.performClick();return true}
    private fun mapped(x:Float,y:Float):Pair<Float,Float>? {
        val displayWidth=minOf(width.toFloat(),height*aspect);val displayHeight=displayWidth/aspect
        val left=(width-displayWidth)/2;val top=(height-displayHeight)/2
        val sceneX=(x-panX-width/2)/zoom+width/2;val sceneY=(y-panY-height/2)/zoom+height/2
        if(sceneX !in left..left+displayWidth || sceneY !in top..top+displayHeight)return null
        return ((sceneX-left)/(displayWidth-resources.displayMetrics.density).coerceAtLeast(1f)).coerceIn(0f,1f) to ((sceneY-top)/(displayHeight-resources.displayMetrics.density).coerceAtLeast(1f)).coerceIn(0f,1f)
    }
    private fun absolute(x:Float,y:Float){mapped(x,y)?.let{(nx,ny)->remote.input(Wire.input(1,flags=8,x=(nx*65535).roundToInt(),y=(ny*65535).roundToInt()))}}
    var onViewChanged:(Float,Float,Float)->Unit={_,_,_->}
    private val points=linkedMapOf<Int,Pair<Float,Float>>()
    private enum class TwoFingerIntent { View, Scroll, IgnorePinch }

    private var armed=false;private var external=false
    private var gestureIntent: TwoFingerIntent? = null
    private var baseX=0f;private var baseY=0f;private var baseDistance=0f;private var baseZoom=1f;private var basePanX=0f;private var basePanY=0f
    private var fractionX=0f;private var fractionY=0f;private var fractionWheel=0f
    private val armDrag=Runnable{if(inputEnabled&&!panOnly&&points.size==1&&!moved&&!remote.mouseHeld)armed=true}
    private fun relative(dx:Float,dy:Float){val density=resources.displayMetrics.density;fractionX+=dx/density*sensitivity;fractionY+=dy/density*sensitivity;val x=fractionX.toInt();val y=fractionY.toInt();fractionX-=x;fractionY-=y;if(x!=0||y!=0)remote.input(Wire.input(1,flags=4,x=x,y=y))}
    private fun resetDesktop(){touchpad.cancel();removeCallbacks(armDrag);if(dragging)remote.key(-1,false);dragging=false;points.clear();multi=false;external=false;armed=false;fractionX=0f;fractionY=0f;fractionWheel=0f;gestureIntent=null}
    private fun desktopTouch(e:MotionEvent):Boolean{
        val density=resources.displayMetrics.density;val index=e.actionIndex;val id=e.getPointerId(index)
        when(e.actionMasked){
            MotionEvent.ACTION_DOWN,MotionEvent.ACTION_POINTER_DOWN->{
                if (e.actionMasked == MotionEvent.ACTION_DOWN) resetDesktop()
                else if (points.isEmpty()) return true
                if(direct&&!panOnly&&!remote.mouseHeld&&points.isEmpty()&&mapped(e.getX(index),e.getY(index))==null)return true
                points[id]=e.getX(index) to e.getY(index)
                if(points.size==1){startX=e.getX(index);startY=e.getY(index);previousX=startX;previousY=startY;moved=false;multi=false;external=!panOnly && remote.mouseHeld;armed=false;gestureIntent=null;if(!panOnly&&!remote.mouseHeld)postDelayed(armDrag,450)}
                else if(points.size==2){removeCallbacks(armDrag);armed=false;if(dragging)remote.key(-1,false);dragging=false;multi=true;gestureIntent=null;val pair=points.values.toList();baseX=(pair[0].first+pair[1].first)/2;baseY=(pair[0].second+pair[1].second)/2;previousX=baseX;previousY=baseY;baseDistance=hypot(pair[0].first-pair[1].first,pair[0].second-pair[1].second);baseZoom=zoom;basePanX=panX+width*(1-zoom)/2;basePanY=panY+height*(1-zoom)/2}
            }
            MotionEvent.ACTION_MOVE->{
                if(points.isEmpty())return true
                val old=points.values.first()
                for(i in 0 until e.pointerCount)if(e.getPointerId(i) in points)points[e.getPointerId(i)]=e.getX(i) to e.getY(i)
                if(points.size>=2){
                    val pair=points.values.take(2);val x=(pair[0].first+pair[1].first)/2;val y=(pair[0].second+pair[1].second)/2;val distance=hypot(pair[0].first-pair[1].first,pair[0].second-pair[1].second)
                    if (!panOnly && (external || (remote.mouseHeld && !dragging))) {
                        external = true
                        relative(x - previousX, y - previousY)
                        previousX = x
                        previousY = y
                        return true
                    }
                    if (gestureIntent == null) {
                        val pinch = abs(distance - baseDistance) > 10 * density
                        val move = hypot(x - baseX, y - baseY) > 12 * density
                        gestureIntent = when {
                            panOnly && (pinch || move) -> TwoFingerIntent.View
                            pinch -> TwoFingerIntent.IgnorePinch
                            move -> TwoFingerIntent.Scroll
                            else -> return true
                        }
                    }
                    when (gestureIntent) {
                        TwoFingerIntent.View -> {
                            val next = (baseZoom * distance / baseDistance.coerceAtLeast(density)).coerceIn(1f, 4f)
                            val px = (x - (baseX - basePanX) * (next / baseZoom)).coerceIn(width * (1 - next), 0f)
                            val py = (y - (baseY - basePanY) * (next / baseZoom)).coerceIn(height * (1 - next), 0f)
                            zoom = next
                            panX = px - width * (1 - next) / 2
                            panY = py - height * (1 - next) / 2
                            onViewChanged(zoom, panX, panY)
                        }
                        TwoFingerIntent.Scroll -> {
                            fractionWheel += -(y - previousY) / density * 2 * sensitivity
                            val wheel = fractionWheel.toInt()
                            if (wheel != 0) {
                                remote.input(Wire.input(6, wheel = wheel))
                                fractionWheel -= wheel
                            }
                        }
                        TwoFingerIntent.IgnorePinch, null -> Unit
                    }
                    previousX=x;previousY=y;return true
                }
                if(multi)return true
                val point=points.values.first();val dx=point.first-old.first;val dy=point.second-old.second
                if(hypot(point.first-startX,point.second-startY)>8*density){moved=true;removeCallbacks(armDrag);if(!panOnly&&armed&&!external&&!remote.mouseHeld){dragging=true;armed=false;if(direct)absolute(startX,startY);remote.key(-1,true);performHapticFeedback(android.view.HapticFeedbackConstants.CLOCK_TICK)}}
                if(panOnly){panX=(panX+dx).coerceIn(width*(1-zoom)/2,width*(zoom-1)/2);panY=(panY+dy).coerceIn(height*(1-zoom)/2,height*(zoom-1)/2);onViewChanged(zoom,panX,panY)}
                else if(external||remote.mouseHeld&&!dragging){external=true;relative(dx,dy)}else if(direct){if(moved||dragging)absolute(point.first,point.second)}else relative(dx,dy)
            }
            MotionEvent.ACTION_UP,MotionEvent.ACTION_POINTER_UP->{
                if(points.remove(id)==null)return true
                removeCallbacks(armDrag);if(points.isNotEmpty())return true
                if(!panOnly){if(dragging){remote.key(-1,false);dragging=false}else if(!external){if(multi){if(gestureIntent==null)remote.input(Wire.input(4,1))}else if(!moved&&!remote.mouseHeld){if(direct)absolute(e.getX(index),e.getY(index));remote.input(Wire.input(4));performClick()}}}
                multi=false;external=false;armed=false;gestureIntent=null
            }
            MotionEvent.ACTION_CANCEL->resetDesktop()
        }
        return true
    }
    override fun onTouchEvent(event:MotionEvent):Boolean {
        if(!inputEnabled){resetDesktop();return true}
        if(desktop)return desktopTouch(event)
        val density=resources.displayMetrics.density
        touchpad.sensitivity=sensitivity
        when(event.actionMasked){
            MotionEvent.ACTION_DOWN,MotionEvent.ACTION_POINTER_DOWN->{val i=event.actionIndex;touchpad.down(event.getPointerId(i),event.getX(i)/density,event.getY(i)/density,event.eventTime,resources.configuration.orientation==android.content.res.Configuration.ORIENTATION_LANDSCAPE&&event.getX(i)>=width/2)}
            MotionEvent.ACTION_MOVE->for(i in 0 until event.pointerCount)touchpad.move(event.getPointerId(i),event.getX(i)/density,event.getY(i)/density)
            MotionEvent.ACTION_UP,MotionEvent.ACTION_POINTER_UP->{val i=event.actionIndex;touchpad.up(event.getPointerId(i),event.getX(i)/density,event.getY(i)/density,event.eventTime);if(event.actionMasked==MotionEvent.ACTION_UP)performClick()}
            MotionEvent.ACTION_CANCEL->touchpad.cancel()
        }
        return true
    }
    override fun onDetachedFromWindow(){resetDesktop();super.onDetachedFromWindow()}
}

