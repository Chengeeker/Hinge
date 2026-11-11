package com.hinge.office

import android.content.res.AssetManager
import java.security.MessageDigest
import java.util.Locale

internal object DeviceMarketNames {
    fun propertyKeys(manufacturer: String, brand: String): List<String> {
        val makers = setOf(manufacturer.lowercase(Locale.ROOT), brand.lowercase(Locale.ROOT))
        val specific = when {
            makers.any { it in setOf("vivo", "iqoo") } -> listOf("ro.vivo.marketname", "ro.vivo.market.name", "ro.vivo.product.marketname")
            makers.any { it in setOf("oppo", "oplus", "oneplus", "realme") } -> listOf("ro.vendor.oplus.market.name", "ro.oplus.market.name", "ro.oppo.market.name")
            makers.any { it in setOf("huawei", "honor") } -> listOf("ro.config.marketing_name")
            else -> emptyList()
        }
        return specific + listOf("ro.product.marketname", "ro.product.market_name",
            "ro.product.odm.marketname", "ro.product.vendor.marketname",
            "ro.product.system.marketname", "ro.product.product.marketname")
    }

    fun lookup(assets: AssetManager, brand: String, manufacturer: String, model: String, device: String): String? {
        val brands = listOf(brand, manufacturer).map { it.trim().lowercase(Locale.ROOT) }.filter { it.isNotEmpty() }.distinct()
        for (maker in brands) {
            val bucket = "%02x".format(MessageDigest.getInstance("SHA-256").digest(maker.toByteArray(Charsets.UTF_8))[0]).take(1)
            val matches = runCatching {
                assets.open("device-names/$bucket.tsv").bufferedReader(Charsets.UTF_8).useLines { lines ->
                    lines.filter { it.startsWith("$maker\t") }.map { it.split('\t') }
                        .filter { it.size == 4 && it[1].equals(model.trim(), ignoreCase = true) }.toList()
                }
            }.getOrDefault(emptyList())
            if (matches.isEmpty()) continue
            val exact = matches.filter { it[2].equals(device.trim(), ignoreCase = true) }
            // A shared model code may represent multiple products. Never guess between them.
            return (exact.ifEmpty { matches }).map { it[3] }.distinct().singleOrNull()
        }
        return null
    }
}
