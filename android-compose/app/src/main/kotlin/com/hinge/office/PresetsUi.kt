@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
package com.hinge.office

import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import org.json.JSONObject
import java.util.UUID
import kotlin.math.roundToInt

@Composable fun PresetsPage(model: HingeModel,desktop:Boolean=false,edit:((GamePreset)->Unit)?=null,preferences:(@Composable ()->Unit)?=null) {
    var document by remember { mutableStateOf<PresetDocument?>(null) }
    var editing by remember { mutableStateOf<GamePreset?>(null) }
    var rename by remember { mutableStateOf<GamePreset?>(null) }
    var namingMode by remember{mutableStateOf("rename")}
    var deletion by remember { mutableStateOf<GamePreset?>(null) }
    LaunchedEffect(Unit) { model.run { document=loadGameDocument(model,desktop) } }
    suspend fun save(next: PresetDocument) { saveGameDocument(model,next,desktop); document = next }
    LaunchedEffect(editing){if(editing!=null&&edit!=null){edit(editing!!);editing=null}}
    editing?.takeIf{edit==null}?.let { preset -> PresetEditor(model, preset, onCancel = { editing = null }, onSave = { value -> model.run {
        val current = requireNotNull(document); save(current.copy(presets = current.presets.map { if (it.id == value.id) value else it })); editing = null
    } }); return }
    val type=MaterialTheme.typography
    val typography=if(desktop)type.copy(titleLarge=type.titleLarge.flutterFontSize(18f),bodyLarge=type.bodyLarge.flutterFontSize(13f),bodyMedium=type.bodyMedium.flutterFontSize(13f),bodySmall=type.bodySmall.flutterFontSize(11f)) else type
    MaterialTheme(typography=typography){ProvideTextStyle(typography.bodyMedium){
    LazyColumn(contentPadding = PaddingValues(16.dp)) {
        item { Row(verticalAlignment=Alignment.CenterVertically){Text(if(desktop)"操控方案" else "游戏预设",modifier=Modifier.weight(1f),style = MaterialTheme.typography.titleLarge);TextButton(onClick={editing=document?.presets?.firstOrNull{it.id==document?.selected}}){Text("编辑布局")}};Text("空白区域滑动：移动视角 · 按住按钮：持续按键 · 多指可同时操作",style=MaterialTheme.typography.bodySmall);Spacer(Modifier.height(8.dp)) }
        item { HingeOutlinedButton(enabled=document!=null,modifier=Modifier.fillMaxWidth(),onClick = { model.run {
            val current = requireNotNull(document); require(current.presets.size < 32) { "最多保存32套预设" }
            val raw = model.api.call("importPreset", channel = "hinge/game_controls") as? String ?: return@run
            val imported = GamePreset.from(JSONObject(raw).toMap()).copy(id = UUID.randomUUID().toString())
            save(current.copy(selected = imported.id, presets = current.presets + imported))
        } }) { HIcon("Icons.file_open_outlined");Spacer(Modifier.width(8.dp));Text("导入预设文件") } }
        items(document?.presets.orEmpty(), key = { it.id }) { preset ->
            var menu by remember { mutableStateOf(false) }
            OriginalRow(preset.name, "${preset.controls.size} 个控件", minHeight=if(desktop)48.dp else null,icon=if(document?.selected==preset.id)"Icons.radio_button_checked" else "Icons.radio_button_off", action = { model.run { save(requireNotNull(document).copy(selected = preset.id)) } }, trailing = {
                Box { IconButton(onClick = { menu = true }) { HIcon("Icons.more_vert", "预设菜单") }; DropdownMenu(menu, { menu = false }, modifier = Modifier.hingeWindowTapFeedback()) {
                    DropdownMenuItem(text = { Text("导出预设文件") }, onClick = { menu = false; model.run { model.api.call("exportPreset", JSONObject(preset.map()).toString(), "hinge/game_controls") } })
                    DropdownMenuItem(text = { Text("重命名") }, onClick = { menu = false; namingMode="rename"; rename = preset })
                    DropdownMenuItem(text = { Text("复制为新预设") }, onClick = { menu=false;namingMode="copy";rename=preset.copy(name="${preset.name} 副本") })
                    DropdownMenuItem(text = { Text("删除") }, onClick = { menu = false; deletion = preset })
                } }
            })
        }
        item { HingeOutlinedButton(enabled=document!=null,modifier=Modifier.fillMaxWidth(),onClick = { model.run { val current = requireNotNull(document); require(current.presets.size < 32) { "最多保存32套预设" }; val preset = (if(desktop)GamePreset.desktop() else defaultGameDocument(model).presets.first()).copy(id=UUID.randomUUID().toString(),name=if(desktop)"新桌面方案" else "新游戏布局"); namingMode="new";rename=preset } }) { HIcon("Icons.add");Spacer(Modifier.width(8.dp));Text(if(desktop)"从桌面布局新建" else "从基础布局新建") } }
        if(preferences!=null)item{preferences()}
    }
    }}
    rename?.let { preset ->
        var name by remember { mutableStateOf(preset.name) }
        HingeAlertDialog(onDismissRequest = { rename = null }, title = { Text("预设名称") }, text = { HingeTextField(name, { name = it.take(40) },autofocus=true,supportingText={Text("${name.length}/40",Modifier.fillMaxWidth(),textAlign=androidx.compose.ui.text.style.TextAlign.End)}) }, confirmButton = { HingeFilledButton(onClick = { model.run { require(name.isNotBlank()); val current = requireNotNull(document); if(namingMode=="rename")save(current.copy(presets = current.presets.map { if (it.id == preset.id) it.copy(name = name.trim()) else it })) else{require(current.presets.size<32){"最多保存32套预设"};val copy=preset.copy(id=UUID.randomUUID().toString(),name=name.trim());save(current.copy(selected=if(namingMode=="new")copy.id else current.selected,presets=current.presets+copy))}; rename = null } }) { Text("确定") } }, dismissButton = { TextButton(onClick = { rename = null }) { Text("取消") } })
    }
    deletion?.let { preset -> ConfirmDelete("删除“${preset.name}”？", { deletion = null }) { model.run { val current = requireNotNull(document); require(current.presets.size > 1) { "至少保留一套预设" }; val remaining = current.presets.filterNot { it.id == preset.id }; save(PresetDocument(if (current.selected == preset.id) remaining.first().id else current.selected, remaining)); deletion = null } } }
}
suspend fun loadGameDocument(model:HingeModel,desktop:Boolean):PresetDocument{
    val raw=model.api.call("load",channel="hinge/game_controls") as? String
    var doc=if(raw==null)defaultGameDocument(model)else PresetDocument.decode(raw)
    if(desktop){
        if(doc.presets.none{it.id=="hinge-desktop-basic"}&&doc.presets.size<32)doc=doc.copy(presets=doc.presets+GamePreset.desktop())
        val selected=model.api.call("loadDesktopSelection",channel="hinge/game_controls") as? String
        doc=doc.copy(selected=selected?.takeIf{id->doc.presets.any{it.id==id}}?:doc.presets.firstOrNull{it.id=="hinge-desktop-basic"}?.id?:doc.presets.first().id)
    }
    return doc
}
suspend fun saveGameDocument(model:HingeModel,doc:PresetDocument,desktop:Boolean){
    if(desktop){
        val raw=model.api.call("load",channel="hinge/game_controls") as? String
        val gameSelection=raw?.let{PresetDocument.decode(it).selected}?:doc.presets.first().id
        model.api.call("saveDesktop",mapOf("json" to doc.copy(selected=gameSelection).encode(),"selected" to doc.selected),"hinge/game_controls")
    }else model.api.call("save",doc.encode(),"hinge/game_controls")
}
suspend fun defaultGameDocument(model: HingeModel): PresetDocument {
    val raw = model.activity.assets.open("default-game-preset.json").bufferedReader().use { it.readText() }
    val preset = GamePreset.from(JSONObject(raw).toMap()).copy(id = UUID.randomUUID().toString())
    return PresetDocument(preset.id, listOf(preset))
}

