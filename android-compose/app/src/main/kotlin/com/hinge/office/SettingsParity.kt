@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class,androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
package com.hinge.office

import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

@Composable fun OriginalAppearance(model:HingeModel,back:()->Unit){
    var fontPicker by remember{mutableStateOf(false)}
    val scheme=MaterialTheme.colorScheme
    val backdrop=rememberGlassBackdrop()
    val density=androidx.compose.ui.platform.LocalDensity.current
    val top=with(density){WindowInsets.statusBars.getTop(this).toDp()}+56.dp
    val bottom=with(density){WindowInsets.navigationBars.getBottom(this).toDp()}
    val titleStyle = MaterialTheme.typography.bodyLarge
    val subtitleStyle = MaterialTheme.typography.bodySmall
    val floating = model.settings["floatingCapsuleNavigation"] == true
    val fontLabels=mapOf(1 to "偏细",0 to "默认",2 to "中等",3 to "偏粗",4 to "加粗")
    fun save(key:String,value:Any)=model.run{model.saveSetting(key,value)}
    @Composable fun section(title:String,content:@Composable ColumnScope.()->Unit){
        Column {
            OriginalSectionHeading(title, Modifier.padding(start = OriginalRowStartInset))
            Spacer(Modifier.height(8.dp))
            OriginalCard(content=content)
        }
    }
    @Composable fun toggle(title:String,key:String,icon:String,subtitle:String?=null,default:Boolean=false){
        OriginalRow(title,subtitle,"Symbols." + icon + "_rounded",trailing={Switch(model.settings[key] as? Boolean?:default,{save(key,it)})},titleStyle=titleStyle,subtitleStyle=subtitleStyle,iconColor=scheme.primary,endPadding=16.dp)
    }
    Box(Modifier.fillMaxSize()){
    LazyColumn(
        modifier = Modifier.fillMaxSize().glassSource(backdrop),
        contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 12.dp + top, bottom = 12.dp + bottom),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        item("theme") { section("明暗模式"){
            listOf("system" to "跟随系统","light" to "浅色模式","dark" to "深色模式").forEach{(key,title)->Row(Modifier.fillMaxWidth().clickable{save("themePreference",key)}.heightIn(min=60.dp).padding(horizontal=OriginalRowStartInset,vertical=2.dp),verticalAlignment=Alignment.CenterVertically){RadioButton(model.settings.text("themePreference","system")==key,{save("themePreference",key)});Spacer(Modifier.width(16.dp));Text(title,style=titleStyle)}}
            toggle("纯黑深色模式","pureBlackDarkMode","contrast")
        } }
        item("colors") { section("色彩方案"){
            toggle("动态取色","dynamicColorEnabled","auto_awesome",default=true)
            if(model.settings["dynamicColorEnabled"]==false){
                Text("Material 3 预置主题色",Modifier.padding(start=16.dp,top=14.dp,end=16.dp,bottom=10.dp),style=MaterialTheme.typography.bodyMedium,color=scheme.onSurfaceVariant)
                FlowRow(Modifier.padding(horizontal=12.dp),horizontalArrangement=Arrangement.spacedBy(12.dp),verticalArrangement=Arrangement.spacedBy(12.dp)){
                    listOf("经典红" to 0xffb3261e,"活力橙" to 0xffff8a00,"极光蓝" to 0xff1976d2,"翡翠绿" to 0xff2e7d32,"优雅紫" to 0xff6750a4,"樱花粉" to 0xffe91e63,"青碧色" to 0xff009688,"经典黑灰" to 0xff546e7a).forEach{(title,color)->FilterChip(selected=(model.settings.number("seedColor",0xff6750a4) and 0xffffffffL)==color,onClick={save("seedColor",color)},label={Text(title)},leadingIcon={Box(Modifier.size(20.dp).background(Color(color),CircleShape))})}
                }
            };Spacer(Modifier.height(8.dp))
        } }
        item("fonts") { section("字体粗细"){
            OriginalRow("自定义应用字体粗细","当前：${fontLabels[model.settings.number("fontWeightLevel").toInt()]?:"默认"}","Symbols.format_bold_rounded",action={fontPicker=true},trailing={HIcon("Symbols.chevron_right_rounded",color=scheme.onSurfaceVariant)},titleStyle=titleStyle,subtitleStyle=subtitleStyle,iconColor=scheme.primary,endPadding=16.dp)
            Column(Modifier.padding(start=16.dp,top=12.dp,end=16.dp,bottom=14.dp).fillMaxWidth().background(scheme.surfaceContainerHighest.copy(alpha=.3f),RoundedCornerShape(12.dp)).padding(12.dp)){
                Text("效果预览",style=MaterialTheme.typography.labelMedium,color=scheme.primary)
                Spacer(Modifier.height(4.dp))
                Text("Hinge 跨设备工作区，让手机与电脑协同更自然。",style=MaterialTheme.typography.bodyMedium)
            }
        } }
        item("navigation") { section("底部导航"){toggle("使用悬浮底栏","floatingCapsuleNavigation","dock")} }
        item("glass") { section("显示效果"){toggle("使用液态玻璃效果","useLiquidGlass","blur_on",if(floating)null else "开启悬浮底栏后生效")} }
        item("feedback") { section("交互反馈"){toggle("震动反馈","hapticFeedbackEnabled","vibration",default=true)} }
    }
    AppearanceHeader(model,backdrop,back)
    }
    if(fontPicker)androidx.compose.ui.window.Dialog(onDismissRequest={fontPicker=false},properties=androidx.compose.ui.window.DialogProperties(usePlatformDefaultWidth=false)){
        Surface(Modifier.padding(horizontal=40.dp,vertical=24.dp).widthIn(min=280.dp,max=560.dp).hingeWindowTapFeedback(),shape=RoundedCornerShape(28.dp),color=scheme.surfaceContainerHigh){Column{
            Text("选择字体粗细",Modifier.padding(start=24.dp,top=24.dp,end=24.dp),style=MaterialTheme.typography.headlineSmall)
            Column(Modifier.weight(1f,fill=false).verticalScroll(rememberScrollState()).padding(vertical=12.dp)){listOf(1,0,2,3,4).forEach{value->Row(Modifier.fillMaxWidth().clickable{save("fontWeightLevel",value);fontPicker=false}.heightIn(min=60.dp).padding(horizontal=20.dp,vertical=2.dp),verticalAlignment=Alignment.CenterVertically){RadioButton(model.settings.number("fontWeightLevel").toInt()==value,null);Spacer(Modifier.width(16.dp));Text(fontLabels.getValue(value),style=MaterialTheme.typography.bodyLarge)}}}
            Row(Modifier.fillMaxWidth().padding(start=24.dp,end=24.dp,bottom=24.dp),horizontalArrangement=Arrangement.End){TextButton(onClick={fontPicker=false}){Text("取消")}}
        }}
    }
}

@Composable fun OriginalKeepAlive(model:HingeModel){
    var status by remember{mutableStateOf<Map<String,Any?>>(emptyMap())}
    var revision by remember{mutableIntStateOf(0)};var loading by remember{mutableStateOf(true)}
    LaunchedEffect(model.foreground,revision){model.run{try{status=model.api.call("keepAliveStatus").asMap()}finally{loading=false}}}
    fun invoke(method:String)=model.run{if(model.api.call(method)==false)model.message="当前设备没有可用的设置页面";revision++}
    @Composable fun checklist(title:String,subtitle:String,icon:String,statusText:String,action:String?=null,onClick:()->Unit){
        Row(Modifier.fillMaxWidth().clickable(onClick=onClick).padding(horizontal=16.dp,vertical=14.dp),verticalAlignment=Alignment.CenterVertically){
            Box(Modifier.width(48.dp),contentAlignment=Alignment.Center){HIcon("Symbols.${icon}_rounded",size=28.dp,color=MaterialTheme.colorScheme.primary)}
            Spacer(Modifier.width(12.dp));Column(Modifier.weight(1f)){Text(title,style=MaterialTheme.typography.titleMedium);Spacer(Modifier.height(4.dp));Text(subtitle,style=MaterialTheme.typography.bodyMedium)}
            Spacer(Modifier.width(12.dp));Box(Modifier.width(104.dp),contentAlignment=Alignment.Center){
                if(action==null)Text(if(loading)"检查中…" else statusText,style=MaterialTheme.typography.labelMedium,textAlign=androidx.compose.ui.text.style.TextAlign.Center)
                else HingeTonalButton(onClick=onClick,modifier=Modifier.widthIn(min=96.dp).height(44.dp),contentPadding=PaddingValues(horizontal=12.dp)){Text(action)}
            }
        }
    }
    OriginalPage{
        OriginalCard{Column(Modifier.padding(16.dp)){
            Text("让 Hinge 在后台保持待命",style=MaterialTheme.typography.titleMedium);Spacer(Modifier.height(6.dp));Text("Hinge 使用 Android 前台服务提供局域网发现、连接恢复和唤醒入口。无操作时会进入低功耗待命；不同厂商还可能需要额外允许后台运行。",style=MaterialTheme.typography.bodyMedium);Spacer(Modifier.height(8.dp))
            OriginalRow("常驻通知",icon="Symbols.notifications_active_rounded",startPadding=0.dp,endPadding=0.dp,iconColor=MaterialTheme.colorScheme.primary,trailing={Switch(status["persistentNotification"]!=false,{enabled->model.run{
                if(enabled&&status["notificationPermission"]!=true&&model.api.call("requestNotificationPermission")!=true){model.message="请先允许通知，常驻通知才能显示"}else model.api.call("setPersistentNotificationEnabled",mapOf("enabled" to enabled));revision++
            }})})
        }};Spacer(Modifier.height(16.dp))
        OriginalCard{Column(Modifier.padding(16.dp)){
            Text("Windows 自动唤醒（实验性）",style=MaterialTheme.typography.titleMedium);Spacer(Modifier.height(6.dp));Text("绑定后，Windows 右键发送文件时会先发一个很短的 BLE 唤醒信号。Android 被系统唤醒后恢复原有局域网连接；BLE 信号不携带文件、验证码或配对码。");Spacer(Modifier.height(10.dp))
            Text(if(status["companionSupported"]!=true)"当前系统没有 Companion Device 支持" else if(status["companionAssociated"]==true)"已绑定 Windows BLE 唤醒设备" else "尚未绑定",style=MaterialTheme.typography.labelLarge)
            Spacer(Modifier.height(8.dp));if(status["companionSupported"]==true){if(status["companionAssociated"]==true)HingeOutlinedButton(enabled=!loading,onClick={invoke("removeCompanion")}){Text("解除绑定")}else HingeTonalButton(enabled=!loading,onClick={invoke("associateCompanion")}){Text("开始绑定")}}
            Spacer(Modifier.height(6.dp));Text("绑定时请先在 Windows Hinge 托盘菜单或设置页开启“60 秒 BLE 配对广播”。",style=MaterialTheme.typography.bodySmall)
        }};Spacer(Modifier.height(16.dp));Text("保活清单",Modifier.padding(horizontal=26.dp),style=MaterialTheme.typography.titleMedium);Spacer(Modifier.height(8.dp))
        OriginalCard{
            val notification=status["notificationPermission"]==true;val battery=status["batteryOptimizationIgnored"]==true
            checklist("通知设置",if(notification)"已允许接收文件和连接状态通知" else "未允许，常驻通知和文件接收提醒无法显示","notifications",if(notification)"已开启" else "未开启",if(notification)null else "去开启"){invoke("openNotificationSettings")}
            checklist("后台高耗电设置",if(battery)"已加入系统电池优化白名单" else "降低后台休眠时被系统限制的概率；不保证永久保持 TCP","battery_saver",if(battery)"已开启" else "未开启",if(battery)null else "去设置"){invoke("openBatteryOptimizationSettings")}
            checklist("锁定后台 / 自启动","打开厂商的后台保护页面，将 Hinge 加入允许后台运行的清单","lock_clock","按设备提供","去设置"){invoke("openBackgroundProtectionSettings")}
            checklist("应用详情","手动检查通知、电池、后台数据和权限状态","settings_applications","系统页面","打开"){invoke("openAppSettings")}
            checklist("复制连接诊断","导出服务、网络、心跳和重连记录，不包含文件内容、验证码或配对码","content_copy","本机日志","复制"){model.run{copyText(model,model.api.call("readConnectionDiagnostics")?.toString().orEmpty())}}
        };Spacer(Modifier.height(16.dp));OriginalCard(color=MaterialTheme.colorScheme.surfaceContainerLow){Text("说明：自动唤醒的目标是“需要发送时再恢复连接”，不是永久维持 TCP。Android 不允许普通应用绕过系统和厂商的后台管理；BLE、局域网或 Companion 不可用时，常驻通知仍是可靠的人工兜底入口。",Modifier.padding(16.dp))}
    }
}

@Composable fun OriginalSms(model:HingeModel){
    var enabled by remember{mutableStateOf(false)};var status by remember{mutableStateOf<Map<String,Any?>>(emptyMap())};var access by remember{mutableStateOf(false)};var revision by remember{mutableIntStateOf(0)};var loading by remember{mutableStateOf(true)};var working by remember{mutableStateOf(false)}
    LaunchedEffect(model.foreground,revision){model.run{try{enabled=model.api.call("smsRelayEnabled")==true;status=model.api.call("smsPermissionStatus").asMap();access=model.api.call("smsNotificationAccessEnabled")==true}finally{loading=false}}}
    OriginalPage{
        OriginalCard{Column(Modifier.padding(16.dp)){
            Text("将新短信转发到 Windows",style=MaterialTheme.typography.titleMedium);Spacer(Modifier.height(8.dp));Text("仅在你主动开启、并且已建立可信 Windows 会话时转发。Hinge 不读取短信历史，也不会把短信保存到本地文件。验证码通知会提供“复制验证码”按钮。");Spacer(Modifier.height(8.dp))
            OriginalRow("短信同步",if(loading)"检查中…" else if(enabled)"已开启，仅发送给当前可信会话" else "已关闭","Symbols.sms_rounded",startPadding=0.dp,endPadding=0.dp,iconColor=MaterialTheme.colorScheme.primary,trailing={Switch(enabled,{value->working=true;model.run{try{
                if(value&&(status["sms"]!=true||status["readSms"]!=true))status=model.api.call("requestSmsPermissions").asMap()
                if(value&&status["sms"]!=true)model.message="未获得“接收短信”权限，短信同步未开启" else enabled=model.api.call("setSmsRelayEnabled",mapOf("enabled" to value))==true
                revision++
            }finally{working=false}}},enabled=!working)})
        }};Spacer(Modifier.height(16.dp))
        OriginalCard{
            listOf(listOf("接收短信","监听新到的 SMS 和验证码短信","sms","sms"),listOf("访问短信/彩信","读取新短信内容，并兼容部分验证码广播限制","mark_email_read","readSms"),listOf("接收彩信",if(status["mms"]==true)"可以提示新到彩信" else "部分系统会限制第三方应用接收彩信广播","perm_media","mms")).forEach{row->OriginalRow(row[0],row[1],"Symbols.${row[2]}_rounded",40.dp,iconColor=MaterialTheme.colorScheme.primary,trailing={Text(if(loading)"检查中…" else if(status[row[3]]==true)"已允许" else "未允许")})}
            OriginalRow("打开应用权限设置",icon="Symbols.settings_rounded",iconColor=MaterialTheme.colorScheme.primary,action={model.run{model.api.call("openAppSettings")}})
            OriginalRow("短信通知读取（兼容模式）","用于厂商系统拦截短信广播时，从短信应用通知中取得新消息","Symbols.notifications_active_rounded",40.dp,iconColor=MaterialTheme.colorScheme.primary,action={if(!access)model.run{model.api.call("openSmsNotificationAccessSettings")}},trailing={Text(if(access)"已开启" else "去开启")})
        };Spacer(Modifier.height(16.dp));HingeTonalButton(onClick={model.run{val sent=model.api.call("sendSmsRelayTestEvent")==true;model.message=if(sent)"测试消息已进入同步链路，请检查 Windows 的应用内提示" else "请先开启短信同步"}},enabled=enabled,modifier=Modifier.fillMaxWidth()){HIcon("Symbols.send_rounded");Spacer(Modifier.width(8.dp));Text("发送测试消息到 Windows")}
        Spacer(Modifier.height(16.dp));OriginalCard(color=MaterialTheme.colorScheme.surfaceContainerLow){Text("说明：Android 将短信和彩信权限列为高敏感权限，部分系统还会限制验证码广播。兼容模式只处理系统短信应用发出的消息通知，忽略其他应用通知；正文和验证码仅在内存中处理，并通过已连接的可信会话发送。",Modifier.padding(16.dp))}
    }
}

@Composable fun OriginalDefaultApps(model:HingeModel){
    val selections=remember{mutableStateMapOf<String,Map<String,Any?>>()};var selected by remember{mutableStateOf<String?>(null)};var options by remember{mutableStateOf<List<Map<String,Any?>>>(emptyList())}
    val loading=remember{mutableStateMapOf<String,Boolean>()}
    val types=listOf("image" to "图片","video" to "视频","file" to "文件")
    LaunchedEffect(Unit){model.run{for((type,_)in types){val current=model.api.call("defaultApp",mapOf("type" to type)) as? String;if(!current.isNullOrEmpty())selections[type]=model.api.call("defaultAppOptions",mapOf("type" to type)).asRows().firstOrNull{it.text("packageName")==current}?:mapOf("packageName" to current,"label" to "已选择应用")}}}
    OriginalPage(bottomPadding=24.dp){Text("用于打开“收到文件”通知和文件传输记录中的本机文件。未选择时由 Android 询问打开方式。",style=MaterialTheme.typography.bodyLarge);Spacer(Modifier.height(16.dp));OriginalCard{types.forEach{(type,title)->OriginalRow(title,selections[type]?.text("label")?:"系统询问","Symbols.${if(type=="image")"image" else if(type=="video")"movie" else "description"}_rounded",iconColor=MaterialTheme.colorScheme.primary,leading=selections[type]?.takeIf{it.text("packageName").isNotEmpty()}?.let{option->{EncodedAppIcon(option.text("iconBase64"))}},trailing={if(loading[type]==true)HingeCircularProgressIndicator(Modifier.size(20.dp),strokeWidth=2.dp)else HIcon("Symbols.chevron_right_rounded")},action={if(loading[type]!=true){loading[type]=true;model.run{try{options=model.api.call("defaultAppOptions",mapOf("type" to type)).asRows();selected=type}finally{loading[type]=false}}}})}}}
    selected?.let{type->ModalBottomSheet(onDismissRequest={selected=null}){LazyColumn(Modifier.hingeWindowTapFeedback().navigationBarsPadding(),contentPadding=PaddingValues(bottom=16.dp)){
        fun choose(option:Map<String,Any?>){model.run{model.api.call("setDefaultApp",mapOf("type" to type,"packageName" to option["packageName"]));selections[type]=option;selected=null}}
        item{OriginalRow("系统询问",icon="Symbols.help_outline_rounded",iconColor=MaterialTheme.colorScheme.primary,action={choose(emptyMap())},trailing={if(selections[type]?.text("packageName").isNullOrEmpty())HIcon("Symbols.check_rounded")})}
        items(options){option->Row(Modifier.fillMaxWidth().clickable{choose(option)}.heightIn(min=72.dp).padding(start=16.dp,end=24.dp,top=8.dp,bottom=8.dp),verticalAlignment=Alignment.CenterVertically){EncodedAppIcon(option.text("iconBase64"));Spacer(Modifier.width(16.dp));Column(Modifier.weight(1f)){Text(option.text("label","未命名应用"),style=MaterialTheme.typography.bodyLarge);Text(option.text("packageName"),style=MaterialTheme.typography.bodyMedium)};if(selections[type]?.text("packageName")==option.text("packageName"))HIcon("Symbols.check_rounded")}}
        if(options.isEmpty())item{Text("没有找到可处理此类型文件的应用。",Modifier.padding(start=24.dp,top=8.dp,end=24.dp,bottom=24.dp))}
    }}}
}
