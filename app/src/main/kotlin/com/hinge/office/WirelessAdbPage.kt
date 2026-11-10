@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
package com.hinge.office

import android.os.Build
import android.widget.Toast
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.unit.dp

@Composable
internal fun WirelessAdbPage(model: HingeModel) {
    var guidance by rememberSaveable { mutableStateOf("") }
    WirelessAdbContent(
        supported = Build.VERSION.SDK_INT >= 30,
        guidance = guidance,
        pair = { scan ->
            val destination = openDebuggingSettings(model.activity, scan)
            guidance = debuggingGuidance(destination, scan)
            // The destination covers Hinge; show the next step there as well as on return.
            if (destination != DebuggingDestination.Scanner) {
                Toast.makeText(model.activity, guidance, Toast.LENGTH_LONG).show()
            }
        },
    )
}

@Composable
internal fun WirelessAdbContent(supported: Boolean, guidance: String, pair: (Boolean) -> Unit) {
    val view = LocalView.current
    val feedback = LocalHingeSettingsClickFeedback.current
    OriginalPage(topPadding = 12.dp) {
        AppearanceTitle("配对连接")
        AppearanceCard {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Text(if (supported) "无线 ADB 配对" else "需要 Android 11 或更高版本",
                    style = MaterialTheme.typography.titleMedium)
                Text("连接同一网络，在手机开发者选项中开启无线调试。已配对的电脑无需重复操作。",
                    style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Text("电脑入口：Hinge 设置 → 传输与连接 → Android 无线 ADB。扫码时先在电脑显示二维码，配对码则由手机生成。",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    HingeFilledButton(enabled = supported, onClick = { feedback(view); pair(true) }) {
                        HIcon("Symbols.qr_code_scanner_rounded", size = 20.dp)
                        Spacer(Modifier.width(8.dp)); Text("扫码配对")
                    }
                    HingeTonalButton(enabled = supported, onClick = { feedback(view); pair(false) }) {
                        HIcon("Symbols.pin_rounded", size = 20.dp)
                        Spacer(Modifier.width(8.dp)); Text("配对码配对")
                    }
                }
                if (guidance.isNotEmpty()) Text(guidance, style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.primary)
            }
        }
    }
}
