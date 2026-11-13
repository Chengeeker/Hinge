@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
package com.hinge.office

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.graphics.BitmapFactory
import android.net.Uri
import android.os.Build
import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.text.DateFormat
import java.util.Date
import java.util.UUID

fun MainActivity.setComposeContent() = setContent {
    val configuration=androidx.compose.ui.platform.LocalConfiguration.current
    val localized=remember(configuration){android.content.res.Configuration(configuration).apply{setLocale(java.util.Locale.SIMPLIFIED_CHINESE)}}
    val context=remember(localized){createConfigurationContext(localized)}
    // ConfigurationContext provides localized resources, but it is not an
    // ActivityResultRegistryOwner. Permission launchers need the real Activity.
    CompositionLocalProvider(
        androidx.compose.ui.platform.LocalConfiguration provides localized,
        androidx.compose.ui.platform.LocalContext provides context,
        androidx.activity.compose.LocalActivityResultRegistryOwner provides this@setComposeContent,
    ) { HingeTheme(model) { HingeApp(model) } }
}

@Composable fun HingeTheme(model: HingeModel, content: @Composable () -> Unit) {
    val dark = when (model.settings.text("themePreference")) {
        "dark" -> true
        "light" -> false
        else -> isSystemInDarkTheme()
    }
    val dynamic = model.settings["dynamicColorEnabled"] != false && Build.VERSION.SDK_INT >= 31
    val seed = model.settings.number("seedColor", 0xFF6750A4)
    var scheme = if (dynamic) { if (dark) dynamicDarkColorScheme(model.activity) else dynamicLightColorScheme(model.activity) }
        else originalPalette(seed, dark)
    val configuration=androidx.compose.ui.platform.LocalConfiguration.current
    val nativeColors=remember(configuration){model.activity.systemDynamicColors().orEmpty()}
    if(dynamic)scheme=scheme.withNativeRoles(nativeColors,dark)
    if (dark && model.settings["pureBlackDarkMode"] == true) scheme = scheme.copy(background=Color.Black,surface=Color.Black,surfaceDim=Color.Black,surfaceBright=Color(0xff1a1a1a),surfaceContainerLowest=Color.Black,surfaceContainerLow=Color(0xff080808),surfaceContainer=Color(0xff101010),surfaceContainerHigh=Color(0xff181818),surfaceContainerHighest=Color(0xff222222))
    val lightBars = scheme.surface.luminance() > .5f
    LaunchedEffect(model.activity, lightBars) { model.activity.applySystemBarAppearance(lightBars) }
    val baseDensity = androidx.compose.ui.platform.LocalDensity.current
    val appScale = hingeAppFontScale(model.settings["fontScale"])
    val fontDensity = remember(baseDensity, appScale) { hingeFontDensity(baseDensity, appScale) }
    ProvideHingeTapFeedback(model.activity::hapticFeedbackEnabled, model.activity::componentTapFeedback) {
    CompositionLocalProvider(
        androidx.compose.ui.platform.LocalDensity provides fontDensity,
        LocalRippleConfiguration provides null,
        LocalOverscrollFactory provides null,
    ) {
        MaterialTheme(colorScheme = scheme, typography = hingeTypography(model.settings.number("fontWeightLevel").toInt(), scheme, if (model.settings["customFontWeight"] as? Boolean ?: (model.settings.number("fontWeightLevel").toInt() != 0)) model.settings.number("fontWeightDelta", hingeLegacyWeightDelta(model.settings.number("fontWeightLevel").toInt()).toLong()).toInt().coerceIn(-300, 300) else 0)) {
            Surface(Modifier.fillMaxSize(), color = scheme.surface, contentColor = scheme.onSurface) {
                ProvideTextStyle(MaterialTheme.typography.bodyMedium, content)
            }
        }
    }
    }
}

private fun <T> hingeTabTween(): TweenSpec<T> = tween(260, easing = HingeSlideEasing)

private enum class PageMotion { Forward, Back, Tab }

private data class PageTarget(
    val page: String,
    val tab: Int,
    val motion: PageMotion,
    val tabDirection: Int,
    val layer: Int,
)

private val rootTabs = listOf("首页", "工作区", "设置")
private val embeddedPages = rootTabs + listOf("已连接的机型", "连接设备", "笔记", "待办", "日历", "相册")
private val pagesWithOwnChrome = setOf("电脑桌面", "游戏模式", "触控板", "通知历史", "传输记录", "个性化")

@Composable fun HingeApp(model: HingeModel) {
    var page by rememberSaveable { mutableStateOf("首页") }
    var tab by rememberSaveable { mutableIntStateOf(0) }
    var routes by rememberSaveable { mutableStateOf(arrayListOf<String>()) }
    var motion by remember { mutableStateOf(PageMotion.Forward) }
    var layer by remember { mutableIntStateOf(0) }
    var tabDirection by remember { mutableIntStateOf(1) }
    var noteAction by remember { mutableIntStateOf(0) }
    var settingsDialog by remember { mutableStateOf("") }

    fun openEmbedded(target: String, destinationTab: Int, nextMotion: PageMotion) {
        if (page == target && tab == destinationTab) return
        motion = nextMotion
        tabDirection = if (destinationTab < tab) -1 else 1
        layer += if (nextMotion == PageMotion.Back) -1 else 1
        tab = destinationTab
        page = target
    }

    fun navigate(target: String) {
        if (target in embeddedPages) openEmbedded(target, tab, PageMotion.Forward)
        else if (target !in routes) routes = ArrayList(routes + target)
    }

    fun selectTab(index: Int) = openEmbedded(rootTabs[index], index, PageMotion.Tab)

    fun closeRoute(expected: String) {
        val next = popHingeRoute(routes, expected)
        if (next !== routes) routes = ArrayList(next)
    }

    val floating = model.settings["floatingCapsuleNavigation"] == true
    val reducedMotion = rememberGlassReducedMotion(model.activity)
    val rtl = androidx.compose.ui.platform.LocalLayoutDirection.current == androidx.compose.ui.unit.LayoutDirection.Rtl
    val pageTransition = updateTransition(PageTarget(page, tab, motion, tabDirection, layer), label = "workspace")
    val routeTransition = updateTransition<List<String>>(routes, label = "pushed routes")
    val movingRoute = routeTransition.currentState != routeTransition.targetState || routeTransition.isRunning
    val overlayPresent = routes.isNotEmpty() || routeTransition.currentState.isNotEmpty() || movingRoute
    val rootVisible = routeTransition.currentState.isEmpty() || routeTransition.targetState.isEmpty()
    val tabBacking = pageTransition.animateFloat(
        transitionSpec = {
            if (reducedMotion || targetState.motion != PageMotion.Tab) snap() else hingeTabTween()
        },
        label = "tab backing",
    ) { it.tab.toFloat() }
    val snackbar = remember { SnackbarHostState() }
    LaunchedEffect(model.message) {
        if (model.message.isNotEmpty()) {
            snackbar.showSnackbar(model.message)
            model.message = ""
        }
    }

    fun backEmbedded() {
        if (page !in rootTabs) openEmbedded(rootTabs[tab], tab, PageMotion.Back)
        else if (page != "首页") openEmbedded("首页", 0, PageMotion.Back)
        else model.run { model.api.call("moveTaskToBack") }
    }

    BackHandler(enabled = !overlayPresent) { backEmbedded() }
    val backdrop = rememberGlassBackdrop()
    Box(Modifier.fillMaxSize()) {
        CompositionLocalProvider(LocalGlassBackdrop provides backdrop) {
            Scaffold(
                modifier = if (overlayPresent) Modifier.clearAndSetSemantics {} else Modifier,
                topBar = {
                    HingePageBar(page, back = if (page in rootTabs) null else ({ backEmbedded() })) {
                        when (page) {
                            "笔记", "待办" -> IconButton(onClick = { noteAction++ }) {
                                HIcon("Symbols.edit_rounded", if (page == "笔记") "新建笔记" else "新建待办")
                            }
                            "日历", "相册" -> Unit
                            else -> IconButton(onClick = { model.refreshDiscovery() }) {
                                HIcon("Symbols.refresh_rounded", "刷新设备发现")
                            }
                        }
                    }
                },
                bottomBar = {
                    if (!floating) NavigationBar(containerColor = MaterialTheme.colorScheme.surface) {
                        rootTabs.forEachIndexed { index, label ->
                            NavigationBarItem(selected = tab == index, onClick = { selectTab(index) },
                                icon = { HIcon(listOf("Symbols.home_rounded", "Symbols.workspaces_rounded",
                                    "Symbols.settings_rounded")[index], filled = tab == index) },
                                label = { Text(label) })
                        }
                    }
                },
            ) { padding ->
                Box(Modifier.fillMaxSize().padding(padding)) {
                    val captureGlass = rootVisible && floating && model.settings["useLiquidGlass"] == true
                    Box(Modifier.fillMaxSize().then(if (captureGlass) Modifier.glassSource(backdrop) else Modifier)) {
                        pageTransition.AnimatedContent(
                            modifier = Modifier.fillMaxSize().clipToBounds(),
                            contentKey = { it.page },
                            transitionSpec = {
                                val direction = if (rtl) -1 else 1
                                val animation = when {
                                    reducedMotion -> EnterTransition.None togetherWith ExitTransition.None
                                    targetState.motion == PageMotion.Tab -> {
                                        val slide = targetState.tabDirection * direction
                                        slideInHorizontally(hingeTabTween()) { it * slide } togetherWith
                                            slideOutHorizontally(hingeTabTween()) { -it * slide }
                                    }
                                    targetState.motion == PageMotion.Back -> EnterTransition.None togetherWith
                                        slideOutHorizontally(tween(220, easing = HingeReturnEasing)) { it * direction }
                                    else -> slideInHorizontally(tween(320, easing = HingeSlideEasing)) { it * direction } togetherWith
                                        ExitTransition.None
                                }
                                animation.targetContentZIndex = targetState.layer.toFloat()
                                animation.using(null)
                            },
                        ) { target ->
                            Box(Modifier.fillMaxSize().background(MaterialTheme.colorScheme.surface)) {
                                when (target.page) {
                                    "首页" -> OriginalHome(model, ::navigate)
                                    "已连接的机型", "连接设备" -> DevicesPage(model, target.page, ::navigate)
                                    "工作区" -> OriginalWorkspace(model, ::navigate)
                                    "设置" -> OriginalSettings(model) {
                                        if (it in listOf("连接安全", "存储路径", "关于")) settingsDialog = it
                                        else navigate(it)
                                    }
                                    "笔记", "待办" -> OriginalNotes(model, target.page == "待办", noteAction)
                                    "日历" -> OriginalCalendar(model)
                                    "相册" -> OriginalPhotos(model)
                                }
                            }
                        }
                    }
                    if (floating) FloatingNavigation(model, tab, tabBacking, ::selectTab, rootVisible, Modifier.align(Alignment.BottomCenter))
                }
            }
        }

        BackHandler(enabled = overlayPresent) {
            if (!movingRoute) routes.lastOrNull()?.let(::closeRoute)
        }
        HingeRouteOverlay(routeTransition, rtl, reducedMotion) { route, active ->
            HingePushedPage(model, route, active, ::navigate) { closeRoute(route) }
        }

        val density = androidx.compose.ui.platform.LocalDensity.current
        val ime = WindowInsets.ime
        val nav = WindowInsets.navigationBars
        val capsuleVisible = !overlayPresent && floating && ime.getBottom(density) == 0
        val bottom = when {
            capsuleVisible -> (if (nav.getBottom(density) > 0) 6.dp else 14.dp) + 76.dp
            !overlayPresent && !floating -> 80.dp
            else -> 0.dp
        }
        SnackbarHost(snackbar, Modifier.align(Alignment.BottomCenter)
            .windowInsetsPadding(nav.union(ime)).padding(bottom = bottom)) { data ->
            Surface(
                Modifier.padding(horizontal = if (capsuleVisible) 16.dp else 0.dp).fillMaxWidth().heightIn(min = 48.dp),
                shape = if (capsuleVisible) RoundedCornerShape(4.dp) else androidx.compose.ui.graphics.RectangleShape,
                color = MaterialTheme.colorScheme.inverseSurface,
            ) {
                Text(data.visuals.message, Modifier.padding(horizontal = 16.dp, vertical = 14.dp),
                    style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.inverseOnSurface)
            }
        }
    }
    model.pairingRequest?.let{peer->var code by remember(peer){mutableStateOf("")};var error by remember(peer){mutableStateOf(false)}
        HingeAlertDialog(onDismissRequest={model.pairingRequest=null},title={Text("连接 ${peer.text("name")}")},text={Column{
            Text("该设备已启用本机配对码，请输入后继续连接。");Spacer(Modifier.height(16.dp))
            HingeTextField(code,{code=it.filter(Char::isDigit).take(6);error=false},autofocus=true,keyboardOptions=androidx.compose.foundation.text.KeyboardOptions(keyboardType=androidx.compose.ui.text.input.KeyboardType.Number),label={Text("6 位数字配对码")},isError=error,supportingText={Row(Modifier.fillMaxWidth()){if(error)Text("请输入完整的 6 位数字");Spacer(Modifier.weight(1f));Text("${code.length}/6")}})
        }},confirmButton={HingeFilledButton(onClick={if(code.length!=6)error=true else{model.connectPeer(peer,code);model.pairingRequest=null}}){Text("连接")}},dismissButton={TextButton(onClick={model.pairingRequest=null}){Text("取消")}})
    }
    OriginalPermissionPrompts(model){navigate("短信同步")}
    when(settingsDialog){
        "连接安全"->PairingSettings(model){settingsDialog=""}
        "存储路径"->StorageDialog(model){settingsDialog=""}
        "关于"->AboutDialog(model){settingsDialog=""}
    }
}

@Composable
private fun HingePageBar(
    page: String,
    back: (() -> Unit)?,
    actions: @Composable RowScope.() -> Unit = {},
) {
    TopAppBar(
        expandedHeight = 56.dp,
        title = { Text(if (page == "连接与保活") "保活设置" else page, style = MaterialTheme.typography.titleLarge) },
        navigationIcon = {
            if (back != null) IconButton(onClick = back) { HIcon("Symbols.arrow_back_rounded", "返回上一页") }
        },
        actions = { actions(); Spacer(Modifier.width(8.dp)) },
        colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.surface),
    )
}

@Composable
private fun HingePushedPage(model: HingeModel, route: String, active: Boolean, open: (String) -> Unit, back: () -> Unit) {
    if (route in pagesWithOwnChrome) {
        when (route) {
            "个性化" -> OriginalAppearance(model, back)
            "通知历史" -> OriginalNotifications(model, back)
            "传输记录" -> OriginalHistory(model, back)
            "电脑桌面", "触控板", "游戏模式" -> DesktopPage(model, route, active, back)
        }
    } else Scaffold(topBar = { HingePageBar(route, back) }) { padding ->
        Box(Modifier.fillMaxSize().padding(padding)) {
            when (route) {
                "投屏与远控" -> OriginalScreenControl(model) { open("手机控制电脑") }
                "无线 ADB" -> WirelessAdbPage(model)
                "剪贴板同步" -> ClipboardSyncPage(model, open)
                "手机控制电脑" -> PhoneRemoteSettings(model, open)
                "连接与保活" -> OriginalKeepAlive(model)
                "短信同步" -> OriginalSms(model)
                "默认应用" -> OriginalDefaultApps(model)
                "Cloud Relay" -> RelaySettingsPage(model)
                "按键预设" -> PresetsPage(model)
            }
        }
    }
}

@Composable fun Entry(title: String, detail: String = "", onClick: (() -> Unit)? = null, trailing: @Composable (() -> Unit)? = null) {
    val modifier = Modifier.fillMaxWidth().then(if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier)
    Surface(modifier, shape = RoundedCornerShape(20.dp), color = MaterialTheme.colorScheme.surfaceContainer) {
        Row(Modifier.padding(16.dp).heightIn(min = 48.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) { Text(title, style = MaterialTheme.typography.titleMedium); if (detail.isNotBlank()) Text(detail, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant) }
            trailing?.invoke()
        }
    }
}
@Composable fun ContentColumn(content: @Composable ColumnScope.() -> Unit) = Column(
    Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp), content = content)

fun copyText(model: HingeModel, value: String) { (model.activity.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("Hinge", value)); model.message = "已复制" }
@Composable fun ConfirmDelete(title: String, dismiss: () -> Unit, confirm: () -> Unit) = HingeAlertDialog(onDismissRequest = dismiss, title = { Text(title) }, confirmButton = { HingeFilledButton(onClick = confirm) { Text("确定") } }, dismissButton = { TextButton(onClick = dismiss) { Text("取消") } })
