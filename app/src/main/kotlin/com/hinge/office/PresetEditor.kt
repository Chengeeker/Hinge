@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class, androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
package com.hinge.office

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlin.math.roundToInt

@Composable fun PresetEditor(model:HingeModel,original:GamePreset,onCancel:()->Unit,onSave:(GamePreset)->Unit) {
    var draft by remember(original){mutableStateOf(original)}
    var selected by remember{mutableStateOf<String?>(null)}
    var properties by remember{mutableStateOf(false)}
    var discard by remember{mutableStateOf(false)}
    var zoom by remember{mutableFloatStateOf(1f)}
    var pan by remember{mutableStateOf(Offset.Zero)}
    val undo=remember{mutableStateListOf<GamePreset>()}
    fun checkpoint(){if(undo.size==20)undo.removeAt(0);undo.add(draft)}
    fun update(c:GameControl){draft=draft.copy(controls=draft.controls.map{if(it.id==c.id)c else it})}
    BackHandler{discard=true}
    if(discard)HingeAlertDialog(onDismissRequest={discard=false},title={Text("放弃未保存的布局修改？")},confirmButton={HingeFilledButton(onClick=onCancel){Text("确定")}},dismissButton={TextButton(onClick={discard=false}){Text("取消")}})
    val colors=MaterialTheme.colorScheme
    BoxWithConstraints(Modifier.fillMaxSize().background(colors.surfaceContainerLow)) {
        val width=maxWidth.value;val height=maxHeight.value
        val current=draft.controls.firstOrNull{it.id==selected}
        Row(Modifier.fillMaxSize()) {
            BoxWithConstraints(Modifier.weight(1f).fillMaxHeight().padding(8.dp).clipToBounds()) {
                val fit=minOf(maxWidth.value/width,maxHeight.value/height)
                val density=LocalDensity.current.density
                Box(Modifier.fillMaxSize().pointerInput(Unit){detectTransformGestures{_,delta,scale,_->zoom=(zoom*scale).coerceIn(1f,3f);pan+=delta}},contentAlignment=Alignment.Center) {
                    Box(Modifier.requiredSize((width*fit).dp,(height*fit).dp).graphicsLayer{scaleX=zoom;scaleY=zoom;translationX=pan.x;translationY=pan.y}.background(colors.surface)) {
                        draft.controls.forEach{control->
                            val rect=controlRect(control,width,height)
                            Surface(Modifier.offset{IntOffset((rect.x*fit*density).roundToInt(),(rect.y*fit*density).roundToInt())}.size((rect.width*fit).dp,(rect.height*fit).dp)
                                .pointerInput(control.id,fit){detectDragGestures(onDragStart={selected=control.id;checkpoint()}){change,delta->change.consume();val c=draft.controls.first{it.id==control.id};update(c.copy(x=(c.x+delta.x/(density*fit*width)).coerceIn(0f,1f),y=(c.y+delta.y/(density*fit*height)).coerceIn(0f,1f)))}}
                                .clickable{selected=control.id},shape=RoundedCornerShape((gameControlRadius(control,rect.width,rect.height)*fit).dp),color=colors.surfaceContainerHighest,
                                border=BorderStroke((if(selected==control.id)2*fit else fit).dp,if(selected==control.id)colors.primary else colors.outlineVariant)) {
                                Box(contentAlignment=Alignment.Center){
                                    if(control.joystick){
                                        listOf(androidx.compose.ui.BiasAlignment(0f,-.8f),androidx.compose.ui.BiasAlignment(-.8f,0f),androidx.compose.ui.BiasAlignment(0f,.8f),androidx.compose.ui.BiasAlignment(.8f,0f)).forEachIndexed{i,align->Text(gameKeyName(control.directions[i]),Modifier.align(align),style=MaterialTheme.typography.labelSmall.copy(fontSize=(11*fit).sp,lineHeight=(11*1.45f*fit).sp,letterSpacing=(.5f*fit).sp))}
                                        Box(Modifier.size((rect.width*.37f*fit).dp).background(colors.primary.copy(alpha=.45f),CircleShape),contentAlignment=Alignment.Center){CompositionLocalProvider(LocalContentColor provides colors.onPrimary){ControlImage(control,scale=fit)}}
                                    }else Box(Modifier.padding((8*fit).dp)){ControlImage(control,scale=fit)}
                                }
                            }
                        }
                        if(current!=null){
                            val rect=controlRect(current,width,height)
                            var resizeWidth by remember(current.id){mutableFloatStateOf(rect.width)}
                            var resizeHeight by remember(current.id){mutableFloatStateOf(rect.height)}
                            Box(Modifier.offset{IntOffset(((rect.x+rect.width-24).coerceIn(0f,(width-48).coerceAtLeast(0f))*fit*density).roundToInt(),((rect.y+rect.height-24).coerceIn(0f,(height-48).coerceAtLeast(0f))*fit*density).roundToInt())}.size((48*fit).dp)
                                .pointerInput(current.id,fit){detectDragGestures(onDragStart={checkpoint();val c=draft.controls.first{it.id==current.id};val r=controlRect(c,width,height);resizeWidth=r.width;resizeHeight=r.height}){change,delta->
                                    change.consume();resizeWidth+=delta.x/(density*fit);resizeHeight+=delta.y/(density*fit)
                                    val c=draft.controls.first{it.id==current.id};val short=minOf(width,height)
                                    val h=resizeHeight.coerceIn(minOf(48f,short*.48f),short*.48f)
                                    val round=c.joystick||c.shape=="circle"||c.shape=="auto"&&kotlin.math.abs(c.aspect-1)<.02f
                                    update(c.copy(size=(h/short).coerceIn(.1f,.48f),aspect=if(round)c.aspect else (resizeWidth.coerceIn(minOf(48f,width),width)/h).coerceIn(.5f,3f)))
                                }},contentAlignment=Alignment.Center){Box(Modifier.size((24*fit).dp).background(colors.primaryContainer,RoundedCornerShape((6*fit).dp)),contentAlignment=Alignment.Center){HIcon("Icons.open_in_full","拖动调整大小",(16*fit).dp)}}
                        }
                    }
                }
            }
            val type=MaterialTheme.typography
            MaterialTheme(typography=type.copy(titleLarge=type.titleLarge.flutterFontSize(16f),titleMedium=type.titleMedium.flutterFontSize(14f),bodyLarge=type.bodyLarge.flutterFontSize(13f),bodyMedium=type.bodyMedium.flutterFontSize(13f))) {ProvideTextStyle(MaterialTheme.typography.bodyMedium){
                Column(Modifier.width(if(width<650)176.dp else 208.dp).fillMaxHeight().background(colors.surfaceContainer)) {
                    Row(verticalAlignment=Alignment.CenterVertically){IconButton(onClick={discard=true}){HIcon("Icons.close","取消编辑",20.dp)};TextButton(onClick={onSave(draft)},modifier=Modifier.weight(1f)){HIcon("Icons.check",size=20.dp);Spacer(Modifier.width(8.dp));Text("保存布局")}}
                    if(properties&&current!=null)Box(Modifier.weight(1f)){ControlProperties(model,current,{update(it)},{properties=false},{draft=draft.copy(controls=draft.controls.filterNot{it.id==current.id});selected=null;properties=false})}
                    else Column(Modifier.weight(1f).verticalScroll(rememberScrollState()).padding(horizontal=8.dp)) {
                        @Composable fun action(label:String,icon:String,enabled:Boolean=true,click:()->Unit){TextButton(onClick=click,enabled=enabled,modifier=Modifier.fillMaxWidth()){HIcon("Icons.$icon",size=20.dp);Spacer(Modifier.width(8.dp));Text(label)}}
                        action("添加按键","add"){if(draft.controls.size>=64)model.message="每套预设最多64个控件" else{checkpoint();val c=GameControl(label="新按键");draft=draft.copy(controls=draft.controls+c);selected=c.id}}
                        action("撤销","undo",undo.isNotEmpty()){draft=undo.removeAt(undo.lastIndex);if(draft.controls.none{it.id==selected})selected=null}
                        Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.Center){IconButton(onClick={zoom=(zoom*1.25f).coerceAtMost(3f)}){HIcon("Icons.zoom_in","放大画布")};IconButton(onClick={zoom=1f;pan=Offset.Zero}){HIcon("Icons.fit_screen","适应画布")}}
                        if(current!=null){
                            Text(current.label.ifEmpty{"未命名按键"},maxLines=2)
                            action("属性","tune"){checkpoint();properties=true}
                            FlowRow(horizontalArrangement=Arrangement.Center){listOf(Triple("arrow_back",-1f,0f),Triple("arrow_forward",1f,0f),Triple("arrow_upward",0f,-1f),Triple("arrow_downward",0f,1f)).forEach{(icon,x,y)->IconButton(onClick={checkpoint();update(current.copy(x=(current.x+x/width).coerceIn(0f,1f),y=(current.y+y/height).coerceIn(0f,1f)))}){HIcon("Icons.$icon","微调按键位置")}}}
                            Text("位置 ${(current.x*100).roundToInt()}%, ${(current.y*100).roundToInt()}%")
                        }else Text("轻点选中按键")
                        Spacer(Modifier.height(12.dp));Text("拖动按键移动，角标调整大小。空白处双指缩放、单指移动画布。")
                    }
                }
            }}
        }
    }
}

@Composable private fun ControlProperties(model:HingeModel,c:GameControl,update:(GameControl)->Unit,close:()->Unit,delete:()->Unit){
    var keyIndex by remember(c.id){mutableIntStateOf(-2)}
    Column(Modifier.fillMaxHeight().verticalScroll(rememberScrollState()).padding(12.dp)){
        Row(verticalAlignment=Alignment.CenterVertically){Text(if(c.joystick)"方向摇杆" else "编辑按钮",Modifier.weight(1f),style=MaterialTheme.typography.titleLarge);IconButton(onClick=close){HIcon("Icons.check","完成")}}
        HingeTextField(c.label,{update(c.copy(label=it.take(64)))},label={Text("显示文字 / 字母 / 数字")},supportingText={Text("${c.label.length}/64",Modifier.fillMaxWidth(),textAlign=androidx.compose.ui.text.style.TextAlign.End)})
        @Composable fun binding(title:String,key:Int,index:Int){OriginalRow(title,gameKeyName(key),startPadding=0.dp,endPadding=0.dp,minHeight=56.dp,verticalInset=(-4).dp,subtitleStyle=MaterialTheme.typography.bodyMedium.flutterFontSize(12f),action={keyIndex=index},trailing={if(!c.joystick)HIcon("Icons.keyboard")})}
        if(c.joystick)c.directions.forEachIndexed{i,key->binding(listOf("向上","向左","向下","向右")[i],key,i)}else binding("电脑按键",c.binding,-1)
        if(!c.joystick){
            var expanded by remember{mutableStateOf(false)}
            val shapes=mapOf("auto" to "按原布局","circle" to "圆形（锁定比例）","rounded" to "圆角矩形","rect" to "直角矩形")
            Box{Row(Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surfaceContainerHighest,RoundedCornerShape(16.dp)).clickable{expanded=true}.padding(horizontal=12.dp,vertical=16.dp),verticalAlignment=Alignment.CenterVertically){Column(Modifier.weight(1f)){Text("控件外观",style=MaterialTheme.typography.bodySmall);Text(shapes[c.shape].orEmpty(),style=MaterialTheme.typography.bodyLarge)};HIcon("Icons.arrow_drop_down")};DropdownMenu(expanded,{expanded=false},modifier=Modifier.hingeWindowTapFeedback()){shapes.forEach{(value,label)->DropdownMenuItem(text={Text(label)},onClick={expanded=false;update(c.copy(shape=value,aspect=if(value=="circle")1f else c.aspect,cornerRadius=if(value=="circle").5f else c.cornerRadius))})}}}
            if(c.shape=="rounded"){Text("圆角比例 ${(c.cornerRadius*100).roundToInt()}%");HingeSlider(c.cornerRadius,{update(c.copy(cornerRadius=it))},valueRange=0f.. .5f)}
        }
        Text("大小 ${(c.size*100).roundToInt()}%");HingeSlider(c.size,{update(c.copy(size=it))},valueRange=.1f.. .48f)
        if(!c.joystick&&c.shape!="circle"){Text("宽高比");HingeSlider(c.aspect,{update(c.copy(aspect=it))},valueRange=.5f..3f)}
        Text("位置也可返回编辑画布后直接拖动")
        Text("水平 ${(c.x*100).roundToInt()}%");HingeSlider(c.x,{update(c.copy(x=it))})
        Text("垂直 ${(c.y*100).roundToInt()}%");HingeSlider(c.y,{update(c.copy(y=it))})
        FlowRow(horizontalArrangement=Arrangement.spacedBy(8.dp)){HingeOutlinedButton(onClick={model.run{val path=model.api.call("icon",channel="hinge/game_controls") as? String;if(path!=null)update(c.copy(image=path))}}){HIcon("Icons.image");Spacer(Modifier.width(8.dp));Text("选择图片")}
        if(c.image!=null)TextButton(onClick={update(c.copy(image=null))}){Text("移除图片")}}
        TextButton(onClick=delete){HIcon("Icons.delete_outline");Spacer(Modifier.width(8.dp));Text("删除控件")}
    }
    if(keyIndex>=-1)KeyPicker(if(keyIndex==-1)c.binding else c.directions[keyIndex],c.joystick,{keyIndex=-2}){key->if(keyIndex==-1)update(c.copy(binding=key))else update(c.copy(directions=c.directions.mapIndexed{i,k->if(i==keyIndex)key else k}));keyIndex=-2}
}
@Composable fun KeyPicker(current:Int,keyboardOnly:Boolean,dismiss:()->Unit,choose:(Int)->Unit){
    var custom by remember{mutableStateOf(false)}
    val keys=remember(keyboardOnly){listOf(0)+(if(keyboardOnly)emptyList()else listOf(-1,-2,-3))+(65..90)+(48..57)+listOf(32,27,9,13,8,16,17,18,160,161,162,163,164,165,91,92,37,38,39,40,33,34,35,36,45,46,20,19,44)+(112..135)+(96..111)+listOf(144,186,187,188,189,190,191,192,219,220,221,222)}
    HingeAlertDialog(onDismissRequest=dismiss,title={Text("映射电脑按键")},text={FlowRow(Modifier.widthIn(max=480.dp).verticalScroll(rememberScrollState()),horizontalArrangement=Arrangement.spacedBy(8.dp),verticalArrangement=Arrangement.spacedBy(8.dp)){keys.forEach{key->FilterChip(key==current,{choose(key)},label={Text(gameKeyName(key))})};AssistChip(onClick={custom=true},label={Text("其他虚拟键码…")})}},confirmButton={TextButton(onClick=dismiss){Text("取消")}})
    if(custom){var value by remember{mutableStateOf("")};HingeAlertDialog(onDismissRequest={custom=false},title={Text("Windows 虚拟键码")},text={HingeTextField(value,{value=it},autofocus=true,keyboardOptions=androidx.compose.foundation.text.KeyboardOptions(keyboardType=androidx.compose.ui.text.input.KeyboardType.Number),supportingText={Text("十进制 8–254（如 176：下一曲）")})},confirmButton={HingeFilledButton(enabled=value.toIntOrNull() in 8..254,onClick={choose(value.toInt());custom=false}){Text("确定")}},dismissButton={TextButton(onClick={custom=false}){Text("取消")}})}
}
