package com.hinge.office

import java.io.IOException
import java.util.concurrent.atomic.AtomicLong

/** Monotonic progress checks, independent of Android codec calls. */
internal class DesktopDecodeState(private val nowMs: () -> Long) {
    @Volatile var pending = 0
        private set
    private var lastProgress = 0L
    val maxPresentationGapNs = AtomicLong(0)
    @Volatile private var lastPresentationNs = 0L

    fun submitted() { if (pending++ == 0) lastProgress = nowMs() }
    fun decoded() { pending = maxOf(0, pending - 1); lastProgress = nowMs() }
    fun reset() { pending = 0; lastPresentationNs = 0; maxPresentationGapNs.set(0) }
    fun presented(nanoTime: Long) {
        val previous = lastPresentationNs
        if (nanoTime <= previous || nanoTime <= 0) return
        if (previous > 0) {
            val gap = nanoTime - previous
            var recorded = maxPresentationGapNs.get()
            while (gap > recorded && !maxPresentationGapNs.compareAndSet(recorded, gap))
                recorded = maxPresentationGapNs.get()
        }
        lastPresentationNs = nanoTime
    }
    fun requireOutputProgress() {
        if (pending > 0 && nowMs() - lastProgress > 1000)
            throw IOException("视频解码器持续无输出，请降低清晰度或帧率后重连")
    }
    fun requireInputProgress(since: Long) {
        if (nowMs() - since > 250)
            throw IOException("视频解码输入等待超时，请降低电脑端清晰度或帧率后重连")
    }
    companion object {
        @JvmStatic fun visibleDimension(size: Int, first: Int?, last: Int?): Int {
            if (size !in 1..8192) throw IOException("视频解码尺寸无效")
            if (first == null || last == null) return size
            if (first !in 0 until size || last !in first until size)
                throw IOException("视频解码裁剪范围无效")
            return last - first + 1
        }
    }
}
