@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
package com.hinge.office

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import java.security.SecureRandom

@Composable fun RelaySettingsPage(model: HingeModel) {
    val settings = model.settings["cloudRelay"].asMap()
    var endpoint by remember(settings) { mutableStateOf(settings.text("endpoint")) }
    var token by remember(settings) { mutableStateOf(settings.text("deviceToken")) }
    var key by remember(settings) { mutableStateOf(settings.text("relayEncryptionKey")) }
    var enabled by remember(settings) { mutableStateOf(settings["enabled"] == true) }
    var admin by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    var status by remember { mutableStateOf("") }
    fun values() = mapOf("endpoint" to endpoint.trim(), "deviceToken" to token.trim(), "relayEncryptionKey" to key.trim(), "enabled" to enabled)
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp)) {
        Text("可选的异步文件中转",style=MaterialTheme.typography.titleMedium);Spacer(Modifier.height(8.dp))
        Text("局域网连接仍然优先。Cloud Relay 只对已经在 Hinge 中信任过的设备生效；Worker 和 R2 只保存加密内容。");Spacer(Modifier.height(16.dp))
        HingeSettingsCard { OriginalRow("启用 Cloud Relay",if(token.isNotEmpty())"当前设备已注册，可在局域网不可用时使用" else "完成 Worker 注册后才能发送或接收",trailing={Switch(enabled,{enabled=it;model.run{model.saveSetting("cloudRelay",values())}},enabled=!busy)}) }
        Spacer(Modifier.height(12.dp));HingeTextField(endpoint,{endpoint=it},keyboardOptions=androidx.compose.foundation.text.KeyboardOptions(keyboardType=androidx.compose.ui.text.input.KeyboardType.Uri),label={Text("Worker 地址")},placeholder={Text("https://hinge-relay.example.workers.dev")},modifier=Modifier.fillMaxWidth())
        Spacer(Modifier.height(12.dp));HingeTextField(key,{key=it},label={Text("Relay 加密密钥")},supportingText={Text("两台已信任设备必须使用同一密钥；密钥不会上传 Worker。")},modifier=Modifier.fillMaxWidth())
        Spacer(Modifier.height(8.dp));FlowRow(horizontalArrangement=Arrangement.spacedBy(8.dp)){
            HingeOutlinedButton(onClick={key=RelayCrypto.encode(ByteArray(32).also{SecureRandom().nextBytes(it)});status="已生成新密钥。请在两台已信任设备上使用同一密钥后再注册。"},enabled=!busy){HIcon("Icons.refresh");Spacer(Modifier.width(8.dp));Text("生成密钥")}
            HingeOutlinedButton(onClick={if(key.isNotBlank()){copyText(model,key);status="Relay 密钥已复制到系统剪贴板。"}}){HIcon("Icons.copy");Spacer(Modifier.width(8.dp));Text("复制密钥")}
        }
        Spacer(Modifier.height(12.dp));HingeTextField(admin,{admin=it},label={Text("部署 Token（仅注册时使用）")},supportingText={Text("注册完成后不会保存部署 Token；普通 API 使用设备 Token。")},visualTransformation=PasswordVisualTransformation(),modifier=Modifier.fillMaxWidth())
        Spacer(Modifier.height(12.dp));@OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
        FlowRow(horizontalArrangement=Arrangement.spacedBy(8.dp),verticalArrangement=Arrangement.spacedBy(8.dp)){
            HingeFilledButton(enabled=!busy,onClick={model.run{busy=true;try{require(endpoint.isNotBlank()&&admin.isNotBlank()&&key.isNotBlank()){ "注册前请填写 Worker 地址、部署 Token 和 Relay 密钥。" };token=Relay(values(),model.identity).register(admin);admin="";enabled=true;model.saveSetting("cloudRelay",values());status="设备已注册；部署 Token 已从输入框清除。"}catch(e:Exception){status="注册失败：${e.message}"}finally{busy=false}}}){HIcon("Icons.app_registration");Spacer(Modifier.width(8.dp));Text(if(token.isEmpty())"注册此设备" else "重新注册此设备")}
            HingeOutlinedButton(enabled=!busy,onClick={model.run{busy=true;try{Relay(values(),model.identity).request("GET","/v1/health",token="",identify=false);status="Worker 可用。"}catch(e:Exception){status="测试失败：${e.message}"}finally{busy=false}}}){Text("测试 Worker")}
            HingeOutlinedButton(enabled=!busy,onClick={model.run{model.saveSetting("cloudRelay",values());status="Cloud Relay 配置已保存。"}}){Text("保存配置")}
        }
        if(busy){Spacer(Modifier.height(16.dp));HingeLinearProgressIndicator(Modifier.fillMaxWidth())}
        if(status.isNotEmpty()){Spacer(Modifier.height(16.dp));Text(status)}
        Spacer(Modifier.height(20.dp));Text("注意：Cloud Relay 是“可靠送达”而不是保证即时唤醒。Android 被系统完全停止时，必须等 Hinge 再次运行或系统允许的任务触发后才会下载。")
    }
}
