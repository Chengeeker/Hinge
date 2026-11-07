@file:OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class,androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
package com.hinge.office

import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawWithCache
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

@Composable fun FloatingNavigation(model:HingeModel, selected:Int, tabBacking:State<Float>, select:(Int)->Unit, visible:Boolean, modifier:Modifier=Modifier) {
    val density=androidx.compose.ui.platform.LocalDensity.current
    if(WindowInsets.ime.getBottom(density)>0)return
    val margin=if(WindowInsets.navigationBars.getBottom(density)>0)6.dp else 14.dp
    BoxWithConstraints(modifier.fillMaxWidth().padding(bottom=margin),contentAlignment=Alignment.Center) {
        val width=minOf(440f,maxWidth.value-40)*2/3
        if(model.settings["useLiquidGlass"]==true){GlassNavigation(model, width.dp, selected, tabBacking, select, visible);return@BoxWithConstraints}
        if(!visible)return@BoxWithConstraints
        val dark=MaterialTheme.colorScheme.surface.luminance()<.5f
        Surface(Modifier.width(width.dp).height(56.dp).capsuleShadow(dark),shape=RoundedCornerShape(32.dp),color=MaterialTheme.colorScheme.surfaceContainerHigh) {
            Box(Modifier.padding(4.dp).clip(RoundedCornerShape(28.dp))) {
                val offset by animateDpAsState(((width-8)/3*selected).dp,tween(240,easing=androidx.compose.animation.core.CubicBezierEasing(.215f,.61f,.355f,1f)),label="navigation")
                Box(Modifier.offset(x=offset).width(((width-8)/3).dp).fillMaxHeight().background(MaterialTheme.colorScheme.primaryContainer,RoundedCornerShape(28.dp)))
                Row { listOf("首页" to "home","工作区" to "workspaces","设置" to "settings").forEachIndexed{index,(label,icon)->
                    val color=if(selected==index)MaterialTheme.colorScheme.onPrimaryContainer else MaterialTheme.colorScheme.onSurfaceVariant
                    Column(Modifier.weight(1f).fillMaxHeight().clickable{select(index)},horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.Center) {
                        HIcon("Symbols.${icon}_rounded",size=22.dp,color=color,filled=selected==index);Spacer(Modifier.height(2.dp));Text(label,color=color,fontSize=11.5.sp,lineHeight=(11.5f*1.43f).sp,fontWeight=if(selected==index)FontWeight.W600 else FontWeight.W500)
                    }
                } }
            }
        }
    }
}
private fun Modifier.capsuleShadow(dark:Boolean)=drawWithCache {
    val sigma=16.dp.toPx()*.57735f+.5f
    val inset=kotlin.math.ceil(sigma*3+4.dp.toPx()).toInt()
    val bitmap=android.graphics.Bitmap.createBitmap(size.width.toInt()+2*inset,size.height.toInt()+2*inset,android.graphics.Bitmap.Config.ARGB_8888)
    val paint=android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG).apply{color=android.graphics.Color.BLACK;alpha=(255*if(dark).35f else .12f).toInt();maskFilter=android.graphics.BlurMaskFilter(sigma,android.graphics.BlurMaskFilter.Blur.NORMAL)}
    android.graphics.Canvas(bitmap).drawRoundRect(inset.toFloat(),inset+4.dp.toPx(),inset+size.width,inset+4.dp.toPx()+size.height,32.dp.toPx(),32.dp.toPx(),paint)
    val shadow=bitmap.asImageBitmap()
    onDrawBehind{drawImage(shadow,Offset(-inset.toFloat(),-inset.toFloat()))}
}
@Composable fun DevicesPage(model:HingeModel,page:String,open:(String)->Unit) {
    var manual by remember{mutableStateOf(false)}
    var device by remember{mutableStateOf<Map<String,Any?>?>(null)}
    val connected=page=="已连接的机型"
    var listener by remember{mutableStateOf<Map<String,Any?>>(emptyMap())}
    LaunchedEffect(model.initialized,model.foreground){model.run{listener=model.api.call("nativeListenerStatus").asMap()}}
    OriginalPage(model.settings["floatingCapsuleNavigation"]==true) {
        Row(verticalAlignment=Alignment.CenterVertically) {
            Column(Modifier.weight(1f)){OriginalSection(page,if(connected)"显示当前已经建立会话的设备" else "仅在同一局域网内发现，不依赖云端账号")}
            if(!connected){IconButton(onClick={manual=true}){HIcon("Symbols.add_link_rounded","通过 IP 地址连接")};IconButton(onClick={model.refreshDiscovery()}){HIcon("Symbols.refresh_rounded","重新广播")}}
        }
        Spacer(Modifier.height(12.dp))
        if(!connected){
            if(model.discoveryError.isNotEmpty())OriginalError("发现服务异常","${model.discoveryError}\n请检查系统防火墙是否允许 UDP 52830。")
            if(listener.text("error").isNotEmpty())OriginalError("会话监听异常","${listener.text("error")}\n电脑无法被手机连接时，请确认 TCP 52831 已放行，并且没有同时运行旧版本客户端。")
            OriginalCard{OriginalRow(if(model.discoveryListening)"正在监听局域网发现" else "发现服务未监听标准端口",if(model.discoveryListening)"每 3 秒发送广播，并每 10 秒补充探测当前 Wi-Fi 子网。" else "当前只能发送探测，无法接收设备回应；这通常是端口冲突或系统权限问题。",if(model.discoveryListening)"Symbols.wifi_rounded" else "Symbols.wifi_off_rounded",iconColor=MaterialTheme.colorScheme.primary)}
            OriginalCard{OriginalRow(if(listener["listening"]==true)"正在监听设备会话" else "设备会话未监听",if(listener["listening"]==true)"TCP 52831 可接受手机主动连接。" else "手机发现电脑后仍连接超时，通常是端口被占用或防火墙未放行。",if(listener["listening"]==true)"Symbols.link_rounded" else "Symbols.link_off_rounded",iconColor=MaterialTheme.colorScheme.primary)}
            Spacer(Modifier.height(16.dp))
        }
        val trusted=model.recentDevices.map{it.text("deviceId")}.toSet()
        val devices=(if(connected)model.ready else (model.devices+model.ready+model.recentDevices).distinctBy{it.text("deviceId")}).sortedWith(compareBy<Map<String,Any?>>{if(it.text("deviceId") in trusted)0 else 1}.thenBy{it.text("name")})
        if(devices.isEmpty()) OriginalEmpty(if(connected)"Symbols.devices_other_rounded" else "Symbols.search_rounded",if(connected)"还没有已连接设备" else "正在搜索附近的设备",if(connected)"在“连接设备”中找到手机后，直接点击“连接”即可开始使用。" else "请确保手机和电脑连在同一个局域网，并让两端应用保持打开。也可以使用右上角的 IP 定向探测。"){
            HingeFilledButton(onClick={if(connected)open("连接设备")else model.refreshDiscovery()}){HIcon(if(connected)"Symbols.add_link_rounded" else "Symbols.refresh_rounded");Spacer(Modifier.width(8.dp));Text(if(connected)"连接设备" else "立即搜索")}
        }
        devices.forEach{peer->
            val session=model.ready.firstOrNull{it.text("deviceId")==peer.text("deviceId")}
            val online=session!=null||model.devices.any{it.text("deviceId")==peer.text("deviceId")}
            val addresses=(peer["networkAddresses"] as? List<*>)?.joinToString(", ")?:peer.text("address",peer.text("remoteAddress"))
            @Composable fun controls(){
                if(connected&&model.connectingDevice==peer.text("deviceId"))HingeCircularProgressIndicator(Modifier.size(24.dp),strokeWidth=2.dp)
                else if(session!=null)HingeOutlinedButton(onClick={model.disconnect(session)}){Text("断开连接")}
                else HingeFilledButton(enabled=online&&model.connectingDevice.isEmpty(),onClick={if(peer["pairingRequired"]==true){device=peer;manual=true}else model.connectPeer(peer)}){Text(if(model.connectingDevice==peer.text("deviceId"))"连接中…" else "连接")}
            }
            OriginalCard{
                if(connected)OriginalRow(connectedDeviceLabel(peer, devices),"已建立 TCP 会话",leading={OriginalDeviceIcon(peer.text("platform"))},startPadding=20.dp,endPadding=20.dp,verticalInset=10.dp,trailing={controls()})
                else Row(Modifier.padding(18.dp),verticalAlignment=Alignment.CenterVertically){
                    OriginalDeviceIcon(peer.text("platform","android"));Spacer(Modifier.width(14.dp))
                    Column(Modifier.weight(1f)){Text(peer.text("name"),style=MaterialTheme.typography.titleMedium);Spacer(Modifier.height(4.dp));Text("${if(session!=null)"已连接" else if(online)"在线" else "离线"} · $addresses",color=MaterialTheme.colorScheme.onSurfaceVariant)}
                    controls()
                }
            };Spacer(Modifier.height(12.dp))
        }
    }
    if(manual){
        var host by remember{mutableStateOf(device?.text("address")?:"")};var code by remember{mutableStateOf("")};var error by remember{mutableStateOf(false)}
        HingeAlertDialog(onDismissRequest={manual=false;device=null},title={Text(if(device==null)"通过 IP 地址连接" else "连接 ${device?.text("name")}")},text={Column{if(device==null)HingeTextField(host,{host=it},keyboardOptions=androidx.compose.foundation.text.KeyboardOptions(keyboardType=androidx.compose.ui.text.input.KeyboardType.Number),label={Text("设备 IP 地址")},placeholder={Text("例如 192.168.1.105")});if(device?.get("pairingRequired")==true){Text("该设备已启用本机配对码，请输入后继续连接。");Spacer(Modifier.height(16.dp));HingeTextField(code,{code=it.filter(Char::isDigit).take(6);error=false},autofocus=true,keyboardOptions=androidx.compose.foundation.text.KeyboardOptions(keyboardType=androidx.compose.ui.text.input.KeyboardType.Number),label={Text("6 位数字配对码")},isError=error,supportingText={Row(Modifier.fillMaxWidth()){if(error)Text("请输入完整的 6 位数字");Spacer(Modifier.weight(1f));Text("${code.length}/6")}})}}},confirmButton={HingeFilledButton(onClick={if(device==null&&host.isBlank())Unit else if(device!=null&&code.length!=6)error=true else{if(device==null)model.probeManualIp(host)else model.connectPeer(device!!,code);manual=false;device=null}}){Text(if(device==null)"探测" else "连接")}},dismissButton={TextButton(onClick={manual=false;device=null}){Text("取消")}})
    }
}
@Composable fun PairingSettings(model:HingeModel,close:()->Unit) {
    var code by remember{mutableStateOf(model.settings.text("localPairingCode"))};var error by remember{mutableStateOf(false)}
    val explanation = "其他设备连接时需验证这 6 位数字。更改或清除配对码会断开当前局域网连接。留空表示关闭。"
    HingeAlertDialog(onDismissRequest=close,title={Text("本机配对码")},text={Column{Text(explanation);Spacer(Modifier.height(16.dp));HingeTextField(code,{code=it.filter(Char::isDigit).take(6);error=false},autofocus=true,keyboardOptions=androidx.compose.foundation.text.KeyboardOptions(keyboardType=androidx.compose.ui.text.input.KeyboardType.Number),label={Text("6 位数字")},isError=error,supportingText={Row(Modifier.fillMaxWidth()){if(error)Text("请输入完整的 6 位数字");Spacer(Modifier.weight(1f));Text("${code.length}/6")}})}},confirmButton={HingeFilledButton(onClick={if(code.isNotEmpty()&&code.length!=6)error=true else model.run{model.saveSetting("localPairingCode",code);close()}}){Text("保存")}},dismissButton={TextButton(onClick=close){Text("取消")}})
}
@Composable fun PhoneRemoteSettings(model:HingeModel,open:(String)->Unit)=OriginalPage(topPadding=12.dp,bottomPadding=24.dp) {
    val enabled=model.settings["phoneRemoteControlEnabled"]==true
    val computers=model.ready.filter{it.text("platform").contains("windows",true)}
    OriginalCard { OriginalRow("启用手机远控电脑","开启后，可用此手机控制已连接的 Windows 电脑。","Icons.touch_app_outlined",trailing={Switch(enabled,{model.run{model.saveSetting("phoneRemoteControlEnabled",it)}})}) }
    Spacer(Modifier.height(12.dp));OriginalCard{Column(Modifier.padding(16.dp)){Text("当前状态",style=MaterialTheme.typography.titleMedium);Spacer(Modifier.height(8.dp));Text(if(!enabled)"已关闭" else if(computers.isEmpty())"已开启 · 等待 Windows 电脑连接" else "已开启 · ${computers.size} 台电脑在线",style=MaterialTheme.typography.bodyLarge);Spacer(Modifier.height(8.dp));Text("单指滑动移鼠标，轻点左键，双指右键/滚动。电脑桌面长按后移动可拖拽，抬手即松开。")}}
    Spacer(Modifier.height(20.dp));Text("已连接的 Windows 电脑",Modifier.padding(horizontal=16.dp),style=MaterialTheme.typography.titleMedium);Spacer(Modifier.height(8.dp))
    if(computers.isEmpty())OriginalCard{OriginalRow("没有已连接的 Windows 电脑","请先在 Hinge 首页连接电脑，再返回此处打开触控板。","Icons.computer_outlined")}
    computers.forEach{peer->
        OriginalCard {
            var menu by remember{mutableStateOf(false)}
            OriginalRow(connectedDeviceLabel(peer, computers),"已连接 · 等待开始控制","Icons.computer_outlined",trailing={Box{IconButton(onClick={menu=true}){HIcon("Icons.more_vert","电脑设置")};DropdownMenu(menu,{menu=false},modifier=Modifier.hingeWindowTapFeedback()){
                DropdownMenuItem(text={Text("清除此电脑的桌面身份")},onClick={menu=false;model.run{model.api.call("forgetPeer",mapOf("peerId" to peer.text("deviceId")),"hinge/desktop_video")}})
                DropdownMenuItem(text={Text("清除此电脑的已保存密钥")},onClick={menu=false;model.run{model.api.call("deleteRemoteControlKey",mapOf("peerId" to peer.text("deviceId")),"hinge/game_controls")}})
            }}})
            FlowRow(Modifier.padding(start=16.dp,end=16.dp,bottom=12.dp),horizontalArrangement=Arrangement.spacedBy(8.dp),verticalArrangement=Arrangement.spacedBy(8.dp)){
                HingeFilledButton(enabled=enabled,onClick={model.selectedPeer=peer;open("电脑桌面")}){HIcon("Icons.desktop_windows",size=18.dp);Spacer(Modifier.width(8.dp));Text("电脑桌面")}
                HingeTonalButton(enabled=enabled,onClick={model.selectedPeer=peer;open("触控板")}){Text("打开触控板")}
            }
        }
    }
    Spacer(Modifier.height(12.dp));Text("电脑端需开启桌面共享与远控。首次连接核对身份码；独立触控板仅在电脑开启可选加密时需要密钥。",style=MaterialTheme.typography.bodySmall,color=MaterialTheme.colorScheme.onSurfaceVariant)
}
