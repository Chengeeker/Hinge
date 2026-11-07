package com.hinge.office

import java.util.concurrent.atomic.AtomicLong

/** Counts new images acquired by Flutter, not repeated paints or physical scanout. */
internal class DesktopTextureStatistics {
    val consumedFrames = AtomicLong(0)
    val maxGapNs = AtomicLong(0)
    @Volatile var available = true
    private var lastImageTimestamp = Long.MIN_VALUE
    private var lastConsumedNs = 0L

    fun consumed(imageTimestamp: Long, nowNs: Long) {
        if (imageTimestamp < 0 || nowNs < 0) { available = false; return }
        available = true
        if (imageTimestamp == lastImageTimestamp) return
        if (lastConsumedNs > 0 && nowNs > lastConsumedNs) {
            val gap = nowNs - lastConsumedNs
            var recorded = maxGapNs.get()
            while (gap > recorded && !maxGapNs.compareAndSet(recorded, gap)) recorded = maxGapNs.get()
        }
        lastImageTimestamp = imageTimestamp
        lastConsumedNs = nowNs
        consumedFrames.incrementAndGet()
    }
}
