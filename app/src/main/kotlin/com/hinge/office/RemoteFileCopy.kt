package com.hinge.office

import android.content.Context
import android.net.Uri
import android.os.Environment
import java.io.File
import java.io.FileOutputStream
import java.io.InputStream

/**
 * Copy a file that the file manager can read into a user-selected directory
 * under primary shared storage. The source may be a MediaStore content URI,
 * so it cannot be treated as a path. Never overwrite an existing destination.
 */
internal fun copyPhoneFile(
    context: Context,
    sourceUriText: String,
    destinationRelativePath: String,
    displayName: String,
): Map<String, Any?> {
    val root = Environment.getExternalStorageDirectory().canonicalFile
    val rel = destinationRelativePath.replace('\\', '/').trim('/')
    require(rel.split('/').none { it == "." || it == ".." }) {
        "不能粘贴到手机存储目录以外的位置"
    }
    val destination = File(root, rel).canonicalFile
    require(destination.path == root.path ||
        destination.path.startsWith(root.path + File.separator)) {
        "目标文件夹超出手机共享存储范围"
    }
    require(destination.isDirectory) { "目标文件夹不存在或没有访问权限" }

    val uri = Uri.parse(sourceUriText)
    val sourceFile = if (uri.scheme.equals("file", ignoreCase = true)) {
        File(uri.path.orEmpty()).canonicalFile.also { source ->
            require(source.path.startsWith(root.path + File.separator) && source.isFile) {
                "源文件不存在，或不属于手机共享存储"
            }
        }
    } else null
    require(sourceFile != null || uri.scheme.equals("content", ignoreCase = true)) {
        "不支持的文件来源"
    }

    val safeName = displayName.trim().takeIf {
        it.isNotBlank() && it != "." && it != ".." &&
            !it.contains('/') && !it.contains('\\') && !it.contains('\u0000')
    } ?: throw IllegalArgumentException("文件名无效")

    val stem = safeName.substringBeforeLast('.', safeName)
    val suffix = if (stem.length == safeName.length) "" else "." + safeName.substringAfterLast('.')
    var copied: File? = null
    for (index in 0..999) {
        val newName = if (index == 0) safeName else "$stem ($index)$suffix"
        val output = File(destination, newName).canonicalFile
        require(output.parentFile?.path == destination.path) {
            "无效的目标文件名"
        }
        if (!output.createNewFile()) continue
        try {
            val input: InputStream = if (sourceFile != null) sourceFile.inputStream()
                else context.contentResolver.openInputStream(uri)
                    ?: throw IllegalArgumentException("无法读取源文件")
            input.use { source ->
                FileOutputStream(output).use { target -> source.copyTo(target, 128 * 1024) }
            }
            copied = output
            break
        } catch (error: Exception) {
            output.delete()
            throw error
        }
    }
    val result = copied ?: throw IllegalStateException("目标文件名已被占用")
    val relative = result.relativeTo(root).invariantSeparatorsPath
    return mapOf(
        "copied" to true, "name" to result.name,
        "relativePath" to relative,
        "absolutePath" to result.absolutePath,
        "bytes" to result.length(),
    )
}
