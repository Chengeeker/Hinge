@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class,androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
package com.hinge.office

import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.selection.toggleable
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

class DesktopOptions {
    var full by mutableStateOf(false);var actions by mutableStateOf(false);var textInput by mutableStateOf(false)
    var direct by mutableStateOf(false);var viewOnly by mutableStateOf(false);var mapping by mutableStateOf(false)
    var sensitivity by mutableFloatStateOf(3f);var zoom by mutableFloatStateOf(1f);var mouseButtons by mutableStateOf(true)
    var editing by mutableStateOf(false);var zoomMode by mutableStateOf(false);var panX by mutableFloatStateOf(0f);var panY by mutableFloatStateOf(0f)
    var gameCamera by mutableStateOf(false);var showStats by mutableStateOf(false);var panelPage by mutableStateOf("操作")
    var opacity by mutableFloatStateOf(.55f);var presetPicker by mutableStateOf(false);var gamePreferences by mutableStateOf(false)
    var gameHaptics by mutableStateOf(true)
    var systemPending by mutableStateOf(false)
}

@Composable
fun DesktopTools(
    model: HingeModel,
    remote: RemoteController,
    state: DesktopOptions,
    presetName: String,
    active: Boolean,
    fullscreen: Boolean,
    showDesktopRails: Boolean,
    exit: () -> Unit,
    toggleFull: () -> Unit,
    enableMapping: (Boolean) -> Unit,
) {
    var text by remember { mutableStateOf("") }
    var power by remember { mutableStateOf<Pair<Int, String>?>(null) }
    val canControl = remote.ready && !state.viewOnly && !state.systemPending
    val canInput = canControl && !state.zoomMode
    val reducedMotion = rememberGlassReducedMotion(model.activity)
    DesktopToolsPanel(state.actions, active, reducedMotion, dismiss = { state.actions = false }) { compact, panelWidth ->
        val width = panelWidth.value
        val inset=if(compact)12.dp else 16.dp
        fun shortcut(keys:List<Int>){if(canInput)remote.shortcut(keys)}
        @Composable fun heading(label:String){Text(label,Modifier.padding(start=inset,top=if(compact)8.dp else 20.dp,end=inset,bottom=if(compact)4.dp else 8.dp),style=MaterialTheme.typography.titleSmall,color=MaterialTheme.colorScheme.onSurfaceVariant)}
        @Composable fun group(content:@Composable ColumnScope.()->Unit){Surface(Modifier.fillMaxWidth().padding(bottom=if(compact)8.dp else 12.dp),shape=RoundedCornerShape(if(compact)16.dp else 24.dp),color=MaterialTheme.colorScheme.surfaceContainerHigh){Column(content=content)}}
        @Composable fun action(label:String,icon:String,selected:Boolean=false,enabled:Boolean=true,click:()->Unit){HingeTonalButton(onClick=click,enabled=enabled,modifier=Modifier.width(if(compact)kotlin.math.floor((width-24-12)/3).dp else 108.dp).heightIn(min=if(compact)64.dp else 96.dp),shape=RoundedCornerShape(if(compact)14.dp else 20.dp),contentPadding=PaddingValues(if(compact)8.dp else 12.dp),colors=ButtonDefaults.filledTonalButtonColors(containerColor=if(selected)MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceContainerHigh,contentColor=if(selected)MaterialTheme.colorScheme.onPrimaryContainer else MaterialTheme.colorScheme.onSurface)){Column(horizontalAlignment=Alignment.CenterHorizontally){HIcon("Icons.$icon",size=if(compact)20.dp else 28.dp);Spacer(Modifier.height(if(compact)4.dp else 8.dp));Text(label,textAlign=TextAlign.Center,style=MaterialTheme.typography.labelMedium)}}}
        @Composable
        fun toggle(title: String, checked: Boolean, onChange: (Boolean) -> Unit,
            subtitle: String? = null, enabled: Boolean = true) {
            OriginalRow(title, subtitle,
                modifier = Modifier.toggleable(checked, enabled = enabled, role = Role.Switch, onValueChange = onChange),
                trailing = { Switch(checked, onCheckedChange = null, enabled = enabled) })
        }
        @Composable fun keyButton(label:String,keys:List<Int>){HingeOutlinedButton(onClick={shortcut(keys)},enabled=canInput,border=null){Text(label)}}
            val typography=if(compact)MaterialTheme.typography.copy(titleLarge=MaterialTheme.typography.titleLarge.flutterFontSize(18f),titleMedium=MaterialTheme.typography.titleMedium.flutterFontSize(14f),titleSmall=MaterialTheme.typography.titleSmall.flutterFontSize(12f),bodyLarge=MaterialTheme.typography.bodyLarge.flutterFontSize(13f),bodyMedium=MaterialTheme.typography.bodyMedium.flutterFontSize(12f),bodySmall=MaterialTheme.typography.bodySmall.flutterFontSize(11f).copy(lineHeight=13.75.sp),labelLarge=MaterialTheme.typography.labelLarge.flutterFontSize(12f),labelMedium=MaterialTheme.typography.labelMedium.flutterFontSize(11f)) else MaterialTheme.typography
            CompositionLocalProvider(LocalHingeCompact provides compact){MaterialTheme(typography=typography){ProvideTextStyle(typography.bodyMedium){Column{
                Row(Modifier.padding(horizontal=if(compact)4.dp else 8.dp,vertical=if(compact)0.dp else 8.dp),verticalAlignment=Alignment.CenterVertically){IconButton(onClick={if(state.panelPage=="操作")state.actions=false else state.panelPage="操作"}){HIcon(if(state.panelPage=="操作")"Icons.close" else "Symbols.arrow_back_rounded",if(state.panelPage=="操作")"关闭工具栏" else "返回操作")};Text(state.panelPage,Modifier.weight(1f),style=MaterialTheme.typography.titleLarge);if(state.panelPage!="操作")IconButton(onClick={state.actions=false}){HIcon("Icons.close","关闭工具栏")}}
                if(!canControl)Text(if(state.systemPending)"系统请求已发送" else "仅观看 · 鼠标和键盘输入已暂停",Modifier.padding(start=inset,end=inset,bottom=if(compact)4.dp else 8.dp),style=MaterialTheme.typography.bodySmall)
                key(state.panelPage) {
                Column(Modifier.weight(1f).verticalScroll(rememberScrollState()).padding(horizontal=inset)){
                    when(state.panelPage){
                        "操作"->{
                            FlowRow(horizontalArrangement=Arrangement.spacedBy(if(compact)6.dp else 8.dp),verticalArrangement=Arrangement.spacedBy(if(compact)8.dp else 16.dp)){
                                action("退出远控","logout"){state.actions=false;exit()};action(if(fullscreen)"退出全屏" else "全屏显示",if(fullscreen)"fullscreen_exit" else "fullscreen"){state.actions=false;toggleFull()}
                                action("仅观看","visibility_outlined",state.viewOnly||!remote.ready,remote.ready&&!state.systemPending){remote.releaseKeys();state.viewOnly=!state.viewOnly;if(state.viewOnly)state.mapping=false}
                            };Spacer(Modifier.height(8.dp))
                            group{
                                toggle("启用按键映射", state.mapping, enableMapping,
                                    subtitle = if (remote.supportsGameKeys) "将常用按键叠加到桌面" else "需要电脑端支持游戏按键与相对鼠标",
                                    enabled = state.mapping || (canControl && remote.supportsGameKeys))
                                if(state.mapping){OriginalRow("切换操控方案","当前方案：$presetName",action={state.actions=false;state.presetPicker=true});OriginalRow("编辑当前方案",action={state.actions=false;state.editing=true});OriginalRow("其他设置",action={state.actions=false;state.gamePreferences=true})}
                            }
                            heading("操控方式");group{
                                val absoluteSupported = model.desktop["absoluteMouse"] == true
                                val direct = state.direct && absoluteSupported
                                DesktopInputModeButtons(
                                    direct, state.mapping && state.gameCamera, canControl,
                                    absoluteSupported, if (compact) 4.dp else 12.dp,
                                ) { value ->
                                    remote.releaseKeys()
                                    state.direct = value
                                    state.gameCamera = false
                                    state.zoomMode = false
                                    model.run {
                                        val peerId = remote.peer.text("deviceId")
                                        model.api.call("saveInputMode",
                                            mapOf("peerId" to "desktopInputMode." + peerId, "direct" to value),
                                            "hinge/desktop_video")
                                    }
                                }
                                Text(if(compact)if(direct)"点按操作 · 滑动移鼠标 · 长按后拖拽" else "滑动移鼠标 · 轻点左键 · 长按后拖拽" else if(direct)"滑动只移动鼠标 · 轻点左键 · 停住长按后移动可拖拽" else "滑动移动指针 · 轻点左键 · 双指轻点右键 · 停住长按后移动可拖拽",Modifier.padding(start=inset,end=inset,bottom=if(compact)4.dp else 12.dp),style=MaterialTheme.typography.bodySmall)
                                if (state.mapping) toggle("游戏视角", state.gameCamera,
                                    onChange = { remote.releaseKeys(); state.gameCamera = it; state.zoomMode = false },
                                    subtitle = "滑动只转视角；操作地图或菜单时关闭", enabled = canControl)
                                toggle("显示鼠标按键", state.mouseButtons, onChange = {
                                    remote.releaseKeys()
                                    state.mouseButtons = it
                                })
                                val sensitivity = "%.1f×".format(state.sensitivity)
                                val slider: @Composable (Modifier) -> Unit = { modifier ->
                                    HingeSlider(state.sensitivity, { state.sensitivity = it },
                                        modifier = modifier, valueRange = 1.5f..5f, steps = 13, enabled = canControl)
                                }
                                if (compact) {
                                    Row(Modifier.padding(horizontal = inset), verticalAlignment = Alignment.CenterVertically) {
                                        Text("灵敏度")
                                        slider(Modifier.weight(1f))
                                        Text(sensitivity)
                                    }
                                } else {
                                    OriginalRow("鼠标灵敏度", endPadding = inset, trailing = { Text(sensitivity) })
                                    slider(Modifier.fillMaxWidth().padding(horizontal = inset))
                                }
                            }
                            group {
                                val links = buildList {
                                    if (!showDesktopRails) {
                                        add("显示" to "Icons.monitor")
                                        add("键盘" to "Icons.keyboard_outlined")
                                    }
                                    add("系统快捷操作" to "Icons.apps")
                                }
                                links.forEach { (label, icon) ->
                                    OriginalRow(label, icon = icon, action = { state.panelPage = label },
                                        showActionChevron = false)
                                }
                            }
                        }
                        "显示"->{heading("画面");group{
                            OriginalRow("当前视频尺寸",trailing={Text(if(model.desktop.number("width")==0L)"等待画面" else "${model.desktop.number("width")} × ${model.desktop.number("height")}")})
                            toggle("缩放 / 平移画面", state.zoomMode,
                                onChange = { remote.releaseKeys(); state.zoomMode = it },
                                subtitle = "双指捏合缩放，拖动平移。切回鼠标操作后，双指滑动滚动。")
                            FlowRow(Modifier.padding(start=inset,end=inset,bottom=inset),horizontalArrangement=Arrangement.spacedBy(8.dp)){listOf(1f,1.5f,2f).forEach{scale->AssistChip(onClick={state.zoom=scale;state.panX=0f;state.panY=0f},border=null,label={Text(if(scale==1f)"适应屏幕" else "${(scale*100).toInt()}%")})}}
                            toggle("性能信息", state.showStats, onChange = { state.showStats = it },
                                subtitle = "显示实际视频输出、解码和帧间隔")
                        };Text("帧率、码率与共享显示器在电脑端的远控设置中调整。",Modifier.padding(horizontal=inset),style=MaterialTheme.typography.bodySmall)}
                        "键盘"->{heading("发送文本");group{
                            HingeTextField(text,{text=it.take(4096)},enabled=canInput,minLines=1,maxLines=4,placeholder={Text("输入到电脑当前光标处")},supportingText={Text(text.length.toString() + "/4096", Modifier.fillMaxWidth(), textAlign=TextAlign.End)},modifier=Modifier.padding(inset).fillMaxWidth())
                            HingeFilledButton(enabled=canInput,onClick={if(text.isNotEmpty()){remote.sendText(text);text=""}},modifier=Modifier.padding(start=inset,end=inset,bottom=inset).fillMaxWidth()){HIcon("Symbols.send_rounded",size=16.dp);Spacer(Modifier.width(8.dp));Text("发送文本")}
                        };heading("常用按键");FlowRow(horizontalArrangement=Arrangement.spacedBy(8.dp),verticalArrangement=Arrangement.spacedBy(8.dp)){
                            listOf("Esc" to 27,"Tab" to 9,"Enter" to 13,"退格" to 8,"Delete" to 46,"空格" to 32,"↑" to 38,"←" to 37,"↓" to 40,"→" to 39).forEach{(label,code)->keyButton(label,listOf(code))};(1..12).forEach{keyButton("F$it",listOf(111+it))}
                        };heading("编辑快捷键");FlowRow(horizontalArrangement=Arrangement.spacedBy(8.dp),verticalArrangement=Arrangement.spacedBy(8.dp)){listOf("复制" to 67,"粘贴" to 86,"剪切" to 88,"全选" to 65,"撤销" to 90,"重做" to 89).forEach{(label,code)->keyButton(label,listOf(17,code))}}}
                        else->{heading("系统快捷操作");FlowRow(horizontalArrangement=Arrangement.spacedBy(if(compact)6.dp else 8.dp),verticalArrangement=Arrangement.spacedBy(if(compact)8.dp else 16.dp)){
                            action("显示桌面","desktop_windows_outlined",enabled=canInput){shortcut(listOf(91,68))};action("所有窗口","grid_view",enabled=canInput){shortcut(listOf(91,9))};action("切换窗口","swap_horiz",enabled=canInput){shortcut(listOf(18,9))};action("资源管理器","folder_outlined",enabled=canInput){shortcut(listOf(91,69))};action("任务管理器","query_stats",enabled=canInput){shortcut(listOf(17,16,27))};action("开始菜单","window",enabled=canInput){shortcut(listOf(91))}
                        };if(model.desktop["systemActions"]==true){heading("电脑电源");group{listOf(1 to "锁定",2 to "重启",3 to "关机").forEach{entry->OriginalRow("${entry.second}电脑",icon="Icons.${if(entry.first==1)"lock_outline" else if(entry.first==2)"restart_alt" else "power_settings_new"}",action={if(canInput){state.actions=false;power=entry}})}}}}
                    }
                    Spacer(Modifier.height(if(compact)12.dp else 24.dp))
                }
                }
            }}}}
    }
    power?.let{(action,label)->HingeAlertDialog(onDismissRequest={power=null},title={Text("${label}电脑？")},text={Text(if(action==1)"电脑将进入锁定界面，当前远控可能中断，需要在电脑上解锁。" else "请先保存电脑上的工作。确认后发送${label}请求，远控将中断；未保存的应用可能阻止操作。")},dismissButton={TextButton(onClick={power=null}){Text("取消")}},confirmButton={HingeFilledButton(onClick={if(canInput){remote.releaseKeys();remote.input(Wire.input(0x13,action));state.systemPending=true;state.actions=false;model.message="已发送${label}请求，请等待电脑处理"};power=null}){Text("确认$label")}})}
}
