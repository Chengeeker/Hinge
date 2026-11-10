package com.hinge.office

import org.json.JSONObject
import java.util.UUID

data class GameControl(
    val id: String = UUID.randomUUID().toString(), val label: String = "按键", val x: Float = .5f, val y: Float = .5f,
    val size: Float = .15f, val aspect: Float = 1f, val shape: String = "auto", val cornerRadius: Float = .12f,
    val binding: Int = 0, val joystick: Boolean = false, val image: String? = null, val directions: List<Int> = listOf(87, 65, 83, 68),
) {
    fun map() = mapOf("id" to id, "label" to label, "x" to x, "y" to y, "size" to size, "aspect" to aspect,
        "shape" to shape, "cornerRadius" to cornerRadius, "binding" to binding, "joystick" to joystick, "image" to image, "directions" to directions)
    companion object {
        fun from(row: Map<String, Any?>): GameControl {
            fun number(key: String, fallback: Float, min: Float, max: Float): Float = (row[key] as? Number)?.toFloat()?.takeIf { it.isFinite() }?.coerceIn(min, max) ?: fallback
            fun key(value: Any?): Int = (value as? Number)?.takeIf { it.toDouble() == it.toInt().toDouble() }?.toInt()?.takeIf { it == 0 || it in 8..254 || it in -3..-1 } ?: 0
            val shape = row.text("shape").takeIf { it in setOf("auto", "circle", "rounded", "rect") } ?: "auto"
            val circle = shape == "circle"
            return GameControl(row.text("id").take(80), row.text("label").take(64), number("x", .5f, 0f, 1f), number("y", .5f, 0f, 1f),
                number("size", .15f, .1f, .48f), if (circle) 1f else number("aspect", 1f, .5f, 3f), shape,
                if (circle) .5f else number("cornerRadius", .12f, 0f, .5f), key(row["binding"]), row["joystick"] == true,
                row.text("image").takeIf { it.contains("/game-controls/") && it.endsWith(".png") },
                (row["directions"] as? List<*>)?.takeIf { it.size == 4 }?.map { key(it).takeIf { k -> k >= 8 } ?: 0 } ?: listOf(87, 65, 83, 68))
        }
    }
}
data class GamePreset(val id: String, val name: String, val controls: List<GameControl>) {
    fun map() = mapOf("id" to id, "name" to name, "controls" to controls.map { it.map() })
    companion object {
        fun from(row: Map<String, Any?>): GamePreset {
            require(row["id"] is String && row["name"] is String && row["controls"] is List<*>) { "无效预设" }
            val raw = row["controls"] as List<*>
            require(raw.size <= 64 && raw.all { it is Map<*, *> && it["id"] is String }) { "无效或超量控件" }
            val controls = raw.asRows().map(GameControl::from)
            require(controls.map { it.id }.toSet().size == controls.size) { "预设包含重复控件" }
            return GamePreset(row.text("id"), row.text("name").take(40), controls)
        }
        fun desktop(): GamePreset = GamePreset("hinge-desktop-basic", "桌面精简布局", listOf(
            GameControl(label = "Esc", binding = 27, x = .06f, y = .18f), GameControl(label = "Tab", binding = 9, x = .06f, y = .40f),
            GameControl(label = "Ctrl", binding = 17, x = .06f, y = .62f), GameControl(label = "左键", binding = -1, x = .06f, y = .84f),
            GameControl(label = "退格", binding = 8, x = .94f, y = .18f), GameControl(label = "Enter", binding = 13, x = .94f, y = .40f),
            GameControl(label = "Alt", binding = 18, x = .94f, y = .62f), GameControl(label = "右键", binding = -2, x = .94f, y = .84f),
        ).map { it.copy(size = .14f, aspect = 1.2f, shape = "rounded") })
    }
}
data class PresetDocument(val selected: String, val presets: List<GamePreset>) {
    fun encode(): String {
        require(presets.size in 1..32 && presets.map { it.id }.toSet().size == presets.size)
        presets.forEach { require(it.controls.size <= 64 && it.controls.map { c -> c.id }.toSet().size == it.controls.size) }
        val json = JSONObject(mapOf("version" to 1, "selected" to selected, "presets" to presets.map { it.map() })).toString()
        require(json.toByteArray().size <= 1024 * 1024)
        return json
    }
    companion object {
        fun decode(raw: String): PresetDocument {
            require(raw.toByteArray().size <= 1024 * 1024)
            val json = JSONObject(raw).toMap()
            val presets = json["presets"].asRows().map(GamePreset::from)
            require(presets.size in 1..32 && presets.map { it.id }.toSet().size == presets.size)
            return PresetDocument(json.text("selected").takeIf { id -> presets.any { it.id == id } } ?: presets.first().id, presets)
        }
    }
}
data class ControlRect(val x: Float, val y: Float, val width: Float, val height: Float)
fun controlRect(control: GameControl, width: Float, height: Float): ControlRect {
    require(width > 0 && height > 0)
    val h = (minOf(width, height) * control.size).coerceIn(minOf(48f, height), height)
    val w = (h * if (control.joystick || control.shape == "circle") 1f else control.aspect).coerceIn(minOf(48f, width), width)
    return ControlRect((control.x * width - w / 2).coerceIn(0f, width - w), (control.y * height - h / 2).coerceIn(0f, height - h), w, h)
}
