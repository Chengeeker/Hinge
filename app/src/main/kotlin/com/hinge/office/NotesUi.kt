@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
package com.hinge.office

import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.UUID

@Composable fun OriginalNotes(model:HingeModel,tasks:Boolean,createAction:Int){
    var rows by remember(tasks){mutableStateOf<List<Map<String,Any?>>>(emptyList())};var revision by remember{mutableIntStateOf(0)}
    var editing by remember{mutableStateOf<Map<String,Any?>?>(null)};var deleting by remember{mutableStateOf<Map<String,Any?>?>(null)}
    var loading by remember{mutableStateOf(false)};var error by remember{mutableStateOf("")}
    val noun=if(tasks)"待办" else "笔记"
    val firstAction=remember{createAction}
    LaunchedEffect(createAction){if(createAction!=firstAction)editing=emptyMap()}
    LaunchedEffect(tasks,revision){
        loading=true
        try {
            val data=model.api.call(if(tasks)"loadTasks" else "loadNotes")
            rows=withContext(Dispatchers.Default){
                val entries=data.asRows()
                if(tasks)entries else entries.sortedWith(compareByDescending<Map<String,Any?>>{it["pinned"]==true}.thenByDescending{it.number("updatedAt")})
            }
            error=""
        }catch(e:kotlinx.coroutines.CancellationException){throw e}catch(e:Exception){error=e.message.orEmpty()}finally{loading=false}
    }
    suspend fun save(row:Map<String,Any?>){model.api.call(if(tasks)"saveTask" else "saveNote",row+mapOf("updatedAt" to System.currentTimeMillis()));revision++}
    val showRows = !loading && error.isEmpty() && rows.isNotEmpty()
    val cardColor = MaterialTheme.colorScheme.surfaceContainer
    val density = LocalDensity.current
    val bottom = if(model.settings["floatingCapsuleNavigation"]==true)
        136.dp + with(density){WindowInsets.navigationBars.getBottom(this).toDp()} else 32.dp
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val inset = maxOf(16.dp, (maxWidth - 1180.dp) / 2)
        LazyColumn(
            Modifier.fillMaxSize(),
            contentPadding = PaddingValues(start=inset,end=inset,top=8.dp,bottom=bottom),
        ) {
            item {
                Text(if(tasks)"待办事项支持新建、完成、编辑和删除" else "笔记支持新建、编辑、置顶和删除",style=MaterialTheme.typography.bodyLarge)
                Spacer(Modifier.height(16.dp))
            }
            item {
                val bottomCorner = if(showRows)0.dp else 20.dp
                Surface(Modifier.fillMaxWidth(), shape=RoundedCornerShape(20.dp,20.dp,bottomCorner,bottomCorner), color=cardColor) {
                    Column(Modifier.padding(start=20.dp,end=20.dp,top=20.dp,bottom=bottomCorner)){
                        Row(verticalAlignment=Alignment.CenterVertically){HIcon(if(tasks)"Symbols.checklist_rounded" else "Symbols.note_rounded",color=MaterialTheme.colorScheme.primary);Spacer(Modifier.width(10.dp));Text(noun,style=MaterialTheme.typography.titleLarge)}
                        Spacer(Modifier.height(8.dp))
                        if(loading)HingeLinearProgressIndicator(Modifier.fillMaxWidth()) else if(error.isNotEmpty())Text("${noun}读取失败：$error",Modifier.padding(vertical=8.dp),color=MaterialTheme.colorScheme.error)
                        else if(rows.isEmpty())Text(if(tasks)"还没有待办，点击右上角添加一项。" else "还没有笔记，点击右上角开始记录。",Modifier.padding(vertical=28.dp))
                    }
                }
            }
            if(showRows) {
                items(rows) { row ->
                    Surface(Modifier.fillMaxWidth(), color=cardColor) {
                        Row(
                            Modifier.padding(horizontal=20.dp).fillMaxWidth()
                                .heightIn(min=if(tasks&&row["dueAt"] !is Number)56.dp else 72.dp)
                                .clickable{editing=row},
                            verticalAlignment=Alignment.CenterVertically,
                        ){
                            if(tasks){
                                Checkbox(row["completed"]==true,{value->model.run{save(row+mapOf("completed" to value))}})
                                Spacer(Modifier.width(16.dp))
                            }
                            Column(Modifier.weight(1f)){
                                Text(row.text("title"),maxLines=if(tasks)Int.MAX_VALUE else 1,overflow=TextOverflow.Ellipsis,style=MaterialTheme.typography.bodyLarge,textDecoration=if(tasks&&row["completed"]==true)TextDecoration.LineThrough else null,color=if(tasks&&row["completed"]==true)MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.onSurface)
                                if(!tasks)Text(row.text("content").ifEmpty{"无正文"},maxLines=2,overflow=TextOverflow.Ellipsis,style=MaterialTheme.typography.bodyMedium,color=MaterialTheme.colorScheme.onSurfaceVariant)
                                else if(row["dueAt"] is Number)Text("截止 ${dateText(row.number("dueAt"))}",style=MaterialTheme.typography.bodyMedium)
                            }
                            Spacer(Modifier.width(16.dp))
                            if(!tasks)IconButton(onClick={model.run{save(row+mapOf("pinned" to (row["pinned"]!=true)))}}){
                                HIcon("Symbols.push_pin_rounded",if(row["pinned"]==true)"取消置顶" else "置顶笔记")
                            }
                            IconButton(onClick={deleting=row}){HIcon("Symbols.delete_rounded","删除$noun")}
                        }
                    }
                }
                item {
                    Surface(shape=RoundedCornerShape(bottomStart=20.dp,bottomEnd=20.dp),color=cardColor) {
                        Spacer(Modifier.fillMaxWidth().height(20.dp))
                    }
                }
            }
        }
    }
    editing?.let{row->
        var title by remember(row){mutableStateOf(row.text("title"))};var body by remember(row){mutableStateOf(row.text("content"))};var due by remember(row){mutableStateOf((row["dueAt"] as? Number)?.toLong())};var pickDate by remember{mutableStateOf(false)}
        HingeAlertDialog(onDismissRequest={editing=null},title={Text((if(row.isEmpty())"新建" else "编辑")+noun)},text={Column(Modifier.verticalScroll(rememberScrollState())){
            HingeTextField(title,{title=it},autofocus=true,label={Text(if(tasks)"待办内容" else "标题")},modifier=Modifier.fillMaxWidth())
            Spacer(Modifier.height(if(tasks)8.dp else 12.dp))
            if(!tasks)HingeTextField(body,{body=it},label={Text("内容")},minLines=4,maxLines=8,modifier=Modifier.fillMaxWidth())
            else OriginalRow(due?.let{"截止 ${dateText(it)}"}?:"无截止日期",icon="Symbols.event_rounded",startPadding=0.dp,endPadding=0.dp,iconColor=MaterialTheme.colorScheme.primary,action={pickDate=true},trailing={if(due==null)HIcon("Symbols.chevron_right_rounded") else IconButton(onClick={due=null}){HIcon("Symbols.close_rounded","清除截止日期")}})
        }},dismissButton={TextButton(onClick={editing=null}){Text("取消")}},confirmButton={HingeFilledButton(onClick={model.run{
            require(if(tasks)title.isNotBlank() else title.isNotBlank()||body.isNotBlank()){if(tasks)"待办内容不能为空" else "标题和内容不能同时为空"}
            save(row+mapOf("id" to row.text("id").ifEmpty{UUID.randomUUID().toString()},"title" to title.trim().ifEmpty{"未命名笔记"})+if(tasks)mapOf("completed" to (row["completed"]==true),"dueAt" to due) else mapOf("content" to body.trim(),"pinned" to (row["pinned"]==true)));editing=null
        }}){Text("保存")}})
        if(pickDate){
            val initial=Instant.ofEpochMilli(due?:System.currentTimeMillis()).atZone(ZoneId.systemDefault()).toLocalDate().atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli()
            val state=rememberDatePickerState(initialSelectedDateMillis=initial,yearRange=2000..2100)
            DatePickerDialog(modifier=Modifier.hingeWindowTapFeedback(),onDismissRequest={pickDate=false},confirmButton={TextButton(onClick={due=state.selectedDateMillis?.let{Instant.ofEpochMilli(it).atZone(ZoneOffset.UTC).toLocalDate().atStartOfDay(ZoneId.systemDefault()).toInstant().toEpochMilli()};pickDate=false}){Text("确定")}},dismissButton={TextButton(onClick={pickDate=false}){Text("取消")}}){DatePicker(state)}
        }
    }
    deleting?.let{row->HingeAlertDialog(onDismissRequest={deleting=null},title={Text("删除$noun")},text={Text("确定删除“${row.text("title")}”吗？")},dismissButton={TextButton(onClick={deleting=null}){Text("取消")}},confirmButton={HingeFilledButton(onClick={model.run{model.api.call(if(tasks)"deleteTask" else "deleteNote",row.text("id"));deleting=null;revision++}}){Text("确定")}})}
}
private fun dateText(value:Long)=Instant.ofEpochMilli(value).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("yyyy/M/d"))
