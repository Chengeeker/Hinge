package com.hinge.office

import android.content.Context
import android.graphics.SurfaceTexture
import android.view.Surface
import android.view.TextureView

class DesktopSurfaceHost(context: Context) : TextureView(context), TextureView.SurfaceTextureListener {
    interface Callback { fun onSurfaceCleanup() }
    @Volatile private var producer: Producer? = null
    private val surfaceLock = Any()
    init { surfaceTextureListener = this }
    fun createSurfaceProducer(): Producer = synchronized(surfaceLock) {
        check(isAvailable) { "显示区域尚未准备好，请稍后重试" }
        check(producer == null) { "上次画面仍在释放" }
        Producer(requireNotNull(surfaceTexture)).also { producer = it }
    }
    inner class Producer(val texture: SurfaceTexture) {
        val surface = Surface(texture)
        private var callback: Callback? = null
        @Volatile var detached = false
        fun id() = 1L
        fun setSize(width: Int, height: Int) { texture.setDefaultBufferSize(width, height) }
        fun setCallback(value: Callback?) { callback = value }
        fun unavailable() { callback?.onSurfaceCleanup() }
        fun release() = synchronized(surfaceLock) { surface.release(); if(detached)texture.release(); if (producer === this) producer = null }
    }
    override fun onSurfaceTextureAvailable(surface: SurfaceTexture, width: Int, height: Int) {
        synchronized(surfaceLock) {
        producer?.takeIf{it.detached}?.let{active->
            setSurfaceTexture(active.texture)
            active.detached=false
        }
        }
    }
    override fun onSurfaceTextureSizeChanged(surface: SurfaceTexture, width: Int, height: Int) = Unit
    override fun onSurfaceTextureUpdated(surface: SurfaceTexture) = Unit
    override fun onSurfaceTextureDestroyed(surface: SurfaceTexture): Boolean {
        synchronized(surfaceLock) {
        val active=producer?:return true
        // Fullscreen reparenting keeps the decoder Surface; the controller owns final release.
        active.detached=true;return false
        }
    }
}
