package com.hinge.office

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

@Composable
internal fun ClipboardSyncPage(model: HingeModel, open: (String) -> Unit) {
    var saving by remember { mutableStateOf(false) }
    ClipboardSyncContent(
        enabled = model.settings["clipboardSyncEnabled"] != false,
        saving = saving,
        change = { enabled ->
            saving = true
            model.run {
                try { model.saveSetting("clipboardSyncEnabled", enabled) }
                finally { saving = false }
            }
        },
        openAdb = { open("无线 ADB") },
    )
}

@Composable
internal fun ClipboardSyncContent(enabled: Boolean, saving: Boolean,
    change: (Boolean) -> Unit, openAdb: () -> Unit) {
    OriginalPage(topPadding = 12.dp) {
        AppearanceCard {
            AppearanceToggle("剪贴板同步", "content_paste", enabled,
                summary = "同步手机与电脑之间复制的文字", enabled = !saving, change = change)
        }
        if (enabled) {
            Spacer(Modifier.height(12.dp))
            Text("连接无线 ADB，并开启电脑端剪贴板同步后生效。",
                Modifier.padding(horizontal = 16.dp), style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            Spacer(Modifier.height(12.dp))
            AppearanceCard {
                OriginalRow("无线 ADB", "前往连接", "Symbols.adb_rounded", action = openAdb,
                    titleStyle = MaterialTheme.typography.bodyLarge, endPadding = 16.dp)
            }
        }
    }
}
