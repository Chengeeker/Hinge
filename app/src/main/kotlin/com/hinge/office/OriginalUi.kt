package com.hinge.office

import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import org.json.JSONObject

internal object OriginalIcons {
    private var codes:JSONObject?=null
    @Synchronized fun load(context:android.content.Context):JSONObject=codes?:JSONObject(context.assets.open("icon-codepoints.json").bufferedReader().use{it.readText()}).also{codes=it}
}

internal fun TextStyle.flutterFontSize(size:Float)=copy(fontSize=size.sp,lineHeight=(lineHeight.value*size/fontSize.value).sp)
internal val LocalHingeCompact = staticCompositionLocalOf { false }
internal val OriginalRowStartInset = 16.dp

@Composable fun HIcon(name: String, description: String? = null, size: Dp = if(LocalHingeCompact.current)20.dp else 24.dp, color: Color = LocalContentColor.current, filled: Boolean = false) {
    val context = LocalContext.current
    val icons = remember { OriginalIcons.load(context) }
    val code = icons.optInt(name, icons.optInt("Symbols.info_rounded", 0xe88e))
    val symbol = name.startsWith("Symbols") || !icons.has(name)
    val font = if (name in listOf("Symbols.home_rounded", "Symbols.workspaces_rounded", "Symbols.settings_rounded")) {
        Font(if (filled) R.font.glass_symbols_filled else R.font.glass_symbols_outline)
    } else if (symbol && filled && android.os.Build.VERSION.SDK_INT >= 26) {
        Font(R.font.material_symbols_rounded, variationSettings = FontVariation.Settings(FontVariation.Setting("FILL", 1f)))
    } else Font(if (symbol) R.font.material_symbols_rounded else R.font.material_icons)
    val density = androidx.compose.ui.platform.LocalDensity.current
    CompositionLocalProvider(androidx.compose.ui.platform.LocalDensity provides androidx.compose.ui.unit.Density(density.density, fontScale = 1f)) {
    Text(String(Character.toChars(code)), modifier = Modifier.size(size).then(if (description != null) Modifier.semantics { contentDescription = description } else Modifier),
        color = color, fontFamily = FontFamily(font),
        fontWeight=FontWeight.Normal, fontSize = size.value.sp, lineHeight = size.value.sp, textAlign = TextAlign.Center, maxLines = 1)
    }
}

@Composable fun originalPalette(seed: Long, dark: Boolean): ColorScheme {
    val context = LocalContext.current
    val palettes = remember { JSONObject(context.assets.open("palettes.json").bufferedReader().use { it.readText() }) }
    val colors = palettes.optJSONObject("${seed and 0xffffffffL}-$dark") ?: palettes.getJSONObject("4284960932-$dark")
    fun c(name: String) = Color(colors.getLong(name).toInt())
    return (if (dark) darkColorScheme() else lightColorScheme()).copy(
        primary=c("primary"), onPrimary=c("onPrimary"), primaryContainer=c("primaryContainer"), onPrimaryContainer=c("onPrimaryContainer"),
        secondary=c("secondary"), onSecondary=c("onSecondary"), secondaryContainer=c("secondaryContainer"), onSecondaryContainer=c("onSecondaryContainer"),
        tertiary=c("tertiary"), onTertiary=c("onTertiary"), tertiaryContainer=c("tertiaryContainer"), onTertiaryContainer=c("onTertiaryContainer"),
        background=c("surface"), surface=c("surface"), onBackground=c("onSurface"), onSurface=c("onSurface"), onSurfaceVariant=c("onSurfaceVariant"),
        surfaceDim=c("surfaceDim"), surfaceBright=c("surfaceBright"), surfaceContainerLowest=c("surfaceContainerLowest"), surfaceContainerLow=c("surfaceContainerLow"),
        surfaceContainer=c("surfaceContainer"), surfaceContainerHigh=c("surfaceContainerHigh"), surfaceContainerHighest=c("surfaceContainerHighest"),
        outline=c("outline"), outlineVariant=c("outlineVariant"), inverseSurface=c("inverseSurface"), inverseOnSurface=c("inverseOnSurface"), inversePrimary=c("inversePrimary"),
        error=c("error"), onError=c("onError"), errorContainer=c("errorContainer"), onErrorContainer=c("onErrorContainer"), scrim=c("scrim"))
}
fun hingeLegacyWeightDelta(level: Int) = listOf(0, -100, 50, 100, 200).getOrElse(level) { 0 }

fun hingeTypography(level: Int, scheme: ColorScheme? = null, adjustment: Int? = null): Typography {
    val delta = adjustment ?: hingeLegacyWeightDelta(level)
    fun role(size: Int, line: Float, weight: Int, spacing: Float = 0f, secondary: Boolean = false) =
        TextStyle(
            fontSize = size.sp,
            lineHeight = line.sp,
            fontWeight = FontWeight(((weight + delta + 50) / 100 * 100).coerceIn(100, 900)),
            letterSpacing = spacing.sp,
            color = if (secondary) scheme?.onSurfaceVariant ?: Color.Unspecified else Color.Unspecified,
        )
    return Typography(
        displayLarge = role(57, 63.84f, 450, -.25f),
        displayMedium = role(45, 52.2f, 450),
        displaySmall = role(36, 43.92f, 450),
        headlineLarge = role(32, 40f, 450),
        headlineMedium = role(28, 36.12f, 450),
        headlineSmall = role(24, 32f, 700),
        titleLarge = role(22, 28f, 700),
        titleMedium = role(18, 24f, 600),
        titleSmall = role(14, 20f, 600, secondary = true),
        bodyLarge = role(16, 24f, 500),
        bodyMedium = role(14, 20f, 400),
        bodySmall = role(12, 17f, 400, secondary = true),
        labelLarge = role(14, 20f, 600),
        labelMedium = role(12, 16f, 500),
        labelSmall = role(11, 16f, 550, .5f),
    )
}
@Composable
internal fun HingeSettingsCard(content: @Composable ColumnScope.() -> Unit) =
    OriginalCard(color = MaterialTheme.colorScheme.settingsCardColor(MaterialTheme.colorScheme.surface), content = content)

@Composable fun OriginalCard(modifier: Modifier = Modifier, color: Color = MaterialTheme.colorScheme.surfaceContainer, content: @Composable ColumnScope.() -> Unit) = Surface(modifier.fillMaxWidth(), shape=RoundedCornerShape(20.dp),color=color) { Column(content=content) }
@Composable fun OriginalRow(title: String, subtitle: String? = null, icon: String? = null, iconSlot: Dp = if(LocalHingeCompact.current)20.dp else 24.dp, action: (() -> Unit)? = null, trailing: @Composable (() -> Unit)? = null, titleStyle: TextStyle = MaterialTheme.typography.bodyLarge, subtitleStyle: TextStyle = MaterialTheme.typography.bodySmall, iconColor: Color = MaterialTheme.colorScheme.onSurfaceVariant, verticalInset: Dp = 0.dp, startPadding: Dp = if(LocalHingeCompact.current)12.dp else OriginalRowStartInset, endPadding: Dp = if(LocalHingeCompact.current)12.dp else 24.dp, leading: @Composable (() -> Unit)? = null, minHeight:Dp?=null, modifier: Modifier = Modifier, showActionChevron: Boolean = true) {
    val rowGap=if(LocalHingeCompact.current)10.dp else 16.dp
    val verticalPadding=if(LocalHingeCompact.current)4.dp else 8.dp
    Row(modifier.fillMaxWidth().then(if(action!=null) Modifier.clickable(onClick=action) else Modifier).heightIn(min=(minHeight?:if(LocalHingeCompact.current)48.dp else if(subtitle==null) 56.dp else 72.dp)+verticalInset*2).padding(start=startPadding,end=endPadding,top=verticalPadding+verticalInset,bottom=verticalPadding+verticalInset),verticalAlignment=Alignment.CenterVertically) {
        if(leading!=null){leading();Spacer(Modifier.width(rowGap))}
        else if(icon!=null) { Box(Modifier.width(iconSlot), contentAlignment=Alignment.Center) { HIcon(icon,color=iconColor) }; Spacer(Modifier.width(rowGap)) }
        Column(Modifier.weight(1f)) { Text(title,style=titleStyle); if(subtitle!=null) Text(subtitle,style=subtitleStyle,color=if(subtitleStyle.color==Color.Unspecified)MaterialTheme.colorScheme.onSurfaceVariant else subtitleStyle.color) }
        if(trailing!=null || (action!=null && showActionChevron))Spacer(Modifier.width(rowGap))
        if(trailing!=null) trailing() else if(action!=null && showActionChevron) HIcon("Symbols.chevron_right_rounded",size=20.dp,color=MaterialTheme.colorScheme.onSurfaceVariant)
    }
}
internal fun sectionHeadingStyle(typography: androidx.compose.material3.Typography) =
    typography.bodyLarge.copy(fontWeight = typography.titleMedium.fontWeight)

@Composable fun OriginalSectionHeading(title: String, modifier: Modifier = Modifier) {
    Text(title, modifier, style = sectionHeadingStyle(MaterialTheme.typography))
}

@Composable fun OriginalSection(title: String, subtitle: String) {
    OriginalSectionHeading(title)
    Spacer(Modifier.height(4.dp))
    Text(subtitle, color = MaterialTheme.colorScheme.onSurfaceVariant)
}
@Composable fun OriginalError(title:String,message:String)=OriginalCard(color=MaterialTheme.colorScheme.errorContainer){OriginalRow(title,message,"Symbols.warning_rounded",iconColor=MaterialTheme.colorScheme.error)}
@Composable fun OriginalEmpty(icon:String,title:String,message:String,action:(@Composable ()->Unit)?=null)=OriginalCard{
    Column(Modifier.fillMaxWidth().padding(16.dp),horizontalAlignment=Alignment.CenterHorizontally){
        HIcon(icon,size=42.dp,color=MaterialTheme.colorScheme.primary);Spacer(Modifier.height(12.dp))
        Text(title,style=MaterialTheme.typography.titleMedium,textAlign=TextAlign.Center);Spacer(Modifier.height(6.dp));Text(message,style=MaterialTheme.typography.bodyMedium,textAlign=TextAlign.Center)
        if(action!=null){Spacer(Modifier.height(18.dp));action()}
    }
}
@Composable fun OriginalDeviceIcon(platform:String,size:Dp=44.dp){
    Box(Modifier.size(size).background(MaterialTheme.colorScheme.primaryContainer,androidx.compose.foundation.shape.CircleShape),contentAlignment=Alignment.Center){
        HIcon(when{platform.contains("android",true)->"Symbols.phone_android_rounded";platform.contains("windows",true)->"Symbols.desktop_windows_rounded";else->"Symbols.devices_other_rounded"},size=size*.55f,color=MaterialTheme.colorScheme.onPrimaryContainer)
    }
}
@Composable fun OriginalPage(floating: Boolean = false, topPadding: Dp = 8.dp, bottomPadding: Dp = 32.dp, content: @Composable ColumnScope.() -> Unit) {
    val density = androidx.compose.ui.platform.LocalDensity.current
    val bottom = if (floating) 136.dp + with(density) { WindowInsets.navigationBars.getBottom(this).toDp() } else bottomPadding
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(start=16.dp,end=16.dp,top=topPadding,bottom=bottom), horizontalAlignment=Alignment.CenterHorizontally) {
        Column(Modifier.widthIn(max=1180.dp).fillMaxWidth(), content=content)
    }
}

@Composable fun OriginalHome(model: HingeModel, open: (String)->Unit) = OriginalPage(model.settings["floatingCapsuleNavigation"] == true) {
    val selected=model.ready.firstOrNull() ?: model.devices.firstOrNull{peer->model.recentDevices.any{it.text("deviceId")==peer.text("deviceId")}} ?: model.devices.firstOrNull{it.text("platform").contains("android",true)} ?: model.devices.firstOrNull() ?: model.recentDevices.firstOrNull()
    Surface(shape=RoundedCornerShape(20.dp),color=MaterialTheme.colorScheme.primaryContainer) {
        Row(Modifier.padding(16.dp)) { Column(Modifier.weight(1f)) {
            Text("Hinge Work",style=MaterialTheme.typography.titleLarge,color=MaterialTheme.colorScheme.onPrimaryContainer)
            Spacer(Modifier.height(8.dp)); Text(if(selected==null) "连接手机后，在一处查看文件、日程和工作内容。" else "当前设备 · ${selected.text("name")}",style=MaterialTheme.typography.bodyMedium,color=MaterialTheme.colorScheme.onPrimaryContainer)
        }; BrandIcon(model) }
    }
    Spacer(Modifier.height(16.dp)); OriginalSection("设备","管理当前会话，或在局域网中连接新设备"); Spacer(Modifier.height(12.dp))
    OriginalCard { OriginalRow("已连接的机型","${model.ready.size} 台设备已连接","Symbols.devices_rounded",action={open("已连接的机型")},titleStyle=MaterialTheme.typography.bodyLarge); OriginalRow("连接设备","发现同一局域网中的手机或电脑","Symbols.add_link_rounded",action={open("连接设备")},titleStyle=MaterialTheme.typography.bodyLarge) }
    if(selected!=null) { Spacer(Modifier.height(16.dp)); DeviceSummary(model,selected,open) }
    Spacer(Modifier.height(24.dp)); OriginalSection("设备操作",if(selected==null) "查看最近的文件收发记录" else "查看文件传输进度和历史记录"); Spacer(Modifier.height(12.dp))
    OriginalCard { Row(Modifier.padding(16.dp)) { AssistChip(onClick={open("传输记录")},label={Text("文件传输记录")},leadingIcon={HIcon("Icons.history_rounded",size=20.dp)}) } }
}
@Composable private fun BrandIcon(model:HingeModel){
    val name="${model.identity.text("manufacturer")} ${model.identity.text("model")}".lowercase()
    val asset=when{ "honor" in name->"honor.png";"vivo" in name->"vivo.png";listOf("xiaomi","redmi","poco").any{it in name}->"xiaomi.png";"samsung" in name||"galaxy" in name->"samsung.png";"huawei" in name->"huawei.png";"oppo" in name->"oppo.png";else->"android-default.jpg"}
    val bitmap by produceState<android.graphics.Bitmap?>(null,asset){value=kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO){model.activity.assets.open("brands/$asset").use{android.graphics.BitmapFactory.decodeStream(it)}}}
    Surface(Modifier.width(56.dp).height(48.dp),shape=RoundedCornerShape(8.dp),color=Color.White){bitmap?.let{androidx.compose.foundation.Image(it.asImageBitmap(),null,Modifier.padding(horizontal=6.dp),contentScale=androidx.compose.ui.layout.ContentScale.Fit)}}
}
@Composable private fun DeviceSummary(model:HingeModel,peer:Map<String,Any?>,open:(String)->Unit){
    var storage by remember{mutableStateOf<Map<String,Any?>>(emptyMap())}
    var failure by remember{mutableStateOf("")};var loading by remember{mutableStateOf(true)}
    val connected=model.ready.any{it.text("deviceId")==peer.text("deviceId")}
    val discovered=model.devices.any{it.text("deviceId")==peer.text("deviceId")}
    LaunchedEffect(peer.text("deviceId")){loading=true;failure="";try{storage=model.api.call("storageInfo").asMap()}catch(e:kotlinx.coroutines.CancellationException){throw e}catch(e:Exception){failure=e.message.orEmpty()}finally{loading=false}}
    OriginalCard{Column(Modifier.padding(16.dp)){
        Row(verticalAlignment=Alignment.CenterVertically){
            OriginalDeviceIcon(peer.text("platform"),32.dp);Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)){
                Text(peer.text("name"),style=MaterialTheme.typography.titleMedium);Spacer(Modifier.height(4.dp))
                Text(if(connected)"已建立会话 · ${peer.text("remoteAddress",peer.text("address"))}" else if(discovered)"已发现 · 可建立会话" else "等待设备上线",color=MaterialTheme.colorScheme.onSurfaceVariant)
            }
            if(connected)HingeOutlinedButton(onClick={model.disconnect(peer)}){HIcon("Symbols.link_off_rounded");Spacer(Modifier.width(8.dp));Text("断开连接")}
            else HingeFilledButton(onClick={if(discovered&&model.connectingDevice.isEmpty()){if(peer["pairingRequired"]==true)model.pairingRequest=peer else model.connectPeer(peer)}else open("连接设备")}){
                HIcon(if(discovered)"Symbols.link_rounded" else "Symbols.search_rounded");Spacer(Modifier.width(8.dp));Text(if(model.connectingDevice==peer.text("deviceId"))"连接中…" else if(discovered)"连接" else "查找设备")
            }
        }
        Spacer(Modifier.height(20.dp));if(loading)HingeLinearProgressIndicator(Modifier.fillMaxWidth())
        if(failure.isNotBlank()){Text(failure,Modifier.padding(vertical=8.dp),color=MaterialTheme.colorScheme.error);Spacer(Modifier.height(10.dp))}
        Text("手机存储",style=MaterialTheme.typography.labelLarge);Spacer(Modifier.height(8.dp))
        if(storage.isNotEmpty()){
            Row(verticalAlignment=Alignment.CenterVertically){HingeLinearProgressIndicator(progress={if(storage.number("totalBytes")>0)(storage.number("usedBytes").toFloat()/storage.number("totalBytes")).coerceIn(0f,1f)else 0f},trackColor=MaterialTheme.colorScheme.surfaceContainerHighest,modifier=Modifier.weight(1f).height(10.dp).clip(RoundedCornerShape(8.dp)));Spacer(Modifier.width(14.dp));Text("${formatBytes(storage.number("usedBytes"))} / ${formatBytes(storage.number("totalBytes"))}",style=MaterialTheme.typography.bodyMedium)}
            Spacer(Modifier.height(5.dp));Text("剩余 ${formatBytes(storage.number("freeBytes"))}",color=MaterialTheme.colorScheme.onSurfaceVariant)
        }else if(!loading)Text("当前设备没有返回存储信息",color=MaterialTheme.colorScheme.onSurfaceVariant)
    }}
}
@Composable fun OriginalWorkspace(model: HingeModel, open: (String)->Unit) {
    var counts by remember { mutableStateOf(listOf(0,0,0,0)) }
    LaunchedEffect(Unit) { model.run {
        counts=listOf(runCatching{model.api.call("loadNotes").asRows().size}.getOrDefault(0),runCatching{model.api.call("loadTasks").asRows().count{it["completed"]!=true}}.getOrDefault(0),runCatching{model.api.call("calendarEvents").asRows().size}.getOrDefault(0),runCatching{model.api.call("photoAlbums").asRows().size}.getOrDefault(0))
    } }
    OriginalPage(model.settings["floatingCapsuleNavigation"]==true) {
        Text("笔记、待办、日历和相册集中在这里",style=MaterialTheme.typography.bodyMedium,color=MaterialTheme.colorScheme.onSurfaceVariant); Spacer(Modifier.height(16.dp))
        OriginalCard {
            listOf(Triple("笔记","${counts[0]} 篇笔记，支持新建、编辑和删除","note"),Triple("待办","${counts[1]} 项待完成任务","checklist"),Triple("日历","${counts[2]} 项手机日程","calendar_month"),Triple("相册","${counts[3]} 个相册集，按最新照片显示封面","photo_library"),Triple("通知历史","收集手机应用通知，支持按时间和应用筛选","notifications")).forEach { (title,subtitle,icon)->OriginalRow(title,subtitle,"Symbols.${icon}_rounded",action={open(title)},titleStyle=MaterialTheme.typography.bodyLarge,endPadding=16.dp) }
        }
    }
}
@Composable fun OriginalSettings(model:HingeModel,open:(String)->Unit)=OriginalPage(model.settings["floatingCapsuleNavigation"]==true) {
    @Composable fun section(title:String, content:@Composable ColumnScope.()->Unit) {
        Text(title,Modifier.padding(start=24.dp),style=MaterialTheme.typography.titleSmall.copy(fontSize=15.sp),color=MaterialTheme.colorScheme.onSurfaceVariant); Spacer(Modifier.height(8.dp)); HingeSettingsCard(content); Spacer(Modifier.height(32.dp))
    }
    @Composable fun row(title:String,subtitle:String?,icon:String,target:String=title)=OriginalRow(title,subtitle,"Symbols.${icon}_rounded",40.dp,action={open(target)},titleStyle=MaterialTheme.typography.bodyLarge,endPadding=16.dp)
    section("常规") {
        row("个性化",null,"palette")
        row("连接安全",if(model.settings.text("localPairingCode").isEmpty()) "未启用本机配对码" else "已启用 6 位本机配对码","password")
        row("存储设置",null,"folder_special","存储路径")
    }
    section("手机服务") {
        row("投屏与远控",null,"cast")
        row("无线 ADB","扫码或使用配对码连接电脑","adb")
        row("剪贴板同步","同步手机与电脑之间复制的文字","content_paste")
        row("Cloud Relay",if(model.settings["cloudRelay"].asMap().text("deviceToken").isEmpty()) "未配置" else null,"cloud")
        row("保活设置",null,"shield","连接与保活")
        row("默认应用",null,"open_in_new")
        row("短信同步",null,"sms")
    }
    section("关于") { row("关于应用","Hinge ${BuildConfig.VERSION_NAME}","info","关于") }
}

fun ColorScheme.withNativeRoles(values:Map<String,Long>,dark:Boolean):ColorScheme {
 val prefix=if(dark)"dark." else "light."
 fun c(name:String, fallback:Color)=values[prefix+name]?.let{Color(it.toInt())}?:fallback
 return copy(
  primary=c("primary",primary),
  onPrimary=c("onPrimary",onPrimary),
  primaryContainer=c("primaryContainer",primaryContainer),
  onPrimaryContainer=c("onPrimaryContainer",onPrimaryContainer),
  secondary=c("secondary",secondary),
  onSecondary=c("onSecondary",onSecondary),
  secondaryContainer=c("secondaryContainer",secondaryContainer),
  onSecondaryContainer=c("onSecondaryContainer",onSecondaryContainer),
  tertiary=c("tertiary",tertiary),
  onTertiary=c("onTertiary",onTertiary),
  tertiaryContainer=c("tertiaryContainer",tertiaryContainer),
  onTertiaryContainer=c("onTertiaryContainer",onTertiaryContainer),
  surface=c("surface",surface),
  onSurface=c("onSurface",onSurface),
  onSurfaceVariant=c("onSurfaceVariant",onSurfaceVariant),
  outline=c("outline",outline),
  outlineVariant=c("outlineVariant",outlineVariant),
  surfaceContainerLowest=c("surfaceContainerLowest",surfaceContainerLowest),
  surfaceContainerLow=c("surfaceContainerLow",surfaceContainerLow),
  surfaceContainer=c("surfaceContainer",surfaceContainer),
  surfaceContainerHigh=c("surfaceContainerHigh",surfaceContainerHigh),
  surfaceContainerHighest=c("surfaceContainerHighest",surfaceContainerHighest),
  surfaceBright=c("surfaceBright",surfaceBright),
  surfaceDim=c("surfaceDim",surfaceDim),
  error=c("error",error),
  onError=c("onError",onError),
  errorContainer=c("errorContainer",errorContainer),
  onErrorContainer=c("onErrorContainer",onErrorContainer),
  inversePrimary=c("inversePrimary",inversePrimary),
  inverseSurface=c("inverseSurface",inverseSurface),
  inverseOnSurface=c("onInverseSurface",inverseOnSurface),
 background=c("surface",surface),onBackground=c("onSurface",onSurface))
}

