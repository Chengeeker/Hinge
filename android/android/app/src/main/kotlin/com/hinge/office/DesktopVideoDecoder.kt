package com.hinge.office

import android.media.MediaCodec
import android.media.MediaFormat
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.os.SystemClock
import android.view.Surface
import java.io.Closeable
import java.io.IOException
import java.util.concurrent.atomic.AtomicLong

/** Owned by the media worker; neither encoded frames nor decoded pixels enter Dart. */
internal class DesktopVideoDecoder(
    private val surface: Surface,
    private val onFormat: (Int, Int) -> Unit,
    private val onFailure: (Exception) -> Unit = {},
    private val onPresented: () -> Unit = {},
) : Closeable {
    @Volatile private var codec: MediaCodec? = null
    private var formatKey: Triple<Int, Int, Int>? = null
    private val renderThread = HandlerThread("HingeDesktopPresented").apply { start() }
    private val outputHandler = Handler(renderThread.looper)
    @Volatile private var closed = false
    @Volatile private var failure: Exception? = null
    private val outputDrain = object : Runnable {
        override fun run() {
            if (closed || failure != null) return
            try { synchronized(this@DesktopVideoDecoder) { if (!closed) codec?.let { drain(it) } } }
            catch (error: Exception) { failure = error; onFailure(error); return }
            if (!closed) outputHandler.postDelayed(this, 5)
        }
    }
    init { outputHandler.post(outputDrain) }
    val presentedFrames = AtomicLong(0)
    val maxPresentedGapNs: AtomicLong get() = progress.maxPresentationGapNs
    @Volatile var submittedFrames = 0L
        private set
    @Volatile var decodedFrames = 0L
        private set
    private val progress = DesktopDecodeState(SystemClock::elapsedRealtime)
    val pendingFrames: Int get() = progress.pending
    @Volatile var decoderName = ""
        private set
    @Volatile var decoderMode = "unknown"
        private set

    @Synchronized fun submit(packet: DesktopVideoPacket, isActive: () -> Boolean) {
        failure?.let { throw it }
        check(!closed)
        val key = Triple(packet.codec, packet.width, packet.height)
        if (formatKey != key) configure(packet, key)
        val decoder = codec ?: throw IOException("Desktop decoder unavailable")
        val waitingSince = SystemClock.elapsedRealtime()
        while (isActive()) {
            drain(decoder)
            val index = decoder.dequeueInputBuffer(10_000)
            if (index < 0) {
                progress.requireInputProgress(waitingSince)
                continue
            }
            val input = decoder.getInputBuffer(index)
                ?: throw IOException("Desktop decoder input unavailable")
            if (input.capacity() < packet.data.size) throw IOException("Desktop access unit exceeds decoder capacity")
            input.clear()
            input.put(packet.data)
            val flags = if (packet.flags and DesktopVideoProtocol.CONFIGURATION != 0 &&
                packet.flags and DesktopVideoProtocol.KEY_FRAME == 0)
                MediaCodec.BUFFER_FLAG_CODEC_CONFIG else 0
            decoder.queueInputBuffer(index, 0, packet.data.size, packet.presentationTimeUs, flags)
            if (flags == 0) {
                submittedFrames++
                progress.submitted()
            }
            drain(decoder)
            return
        }
    }

    @Synchronized fun drain() { codec?.let { drain(it) } }

    private fun drain(decoder: MediaCodec) {
        val info = MediaCodec.BufferInfo()
        while (true) {
            when (val index = decoder.dequeueOutputBuffer(info, 0)) {
                MediaCodec.INFO_TRY_AGAIN_LATER -> {
                    progress.requireOutputProgress()
                    return
                }
                MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                    val format = decoder.outputFormat
                    fun visibleDimension(sizeKey: String, firstKey: String, lastKey: String): Int {
                        return DesktopDecodeState.visibleDimension(format.getInteger(sizeKey),
                            if (format.containsKey(firstKey)) format.getInteger(firstKey) else null,
                            if (format.containsKey(lastKey)) format.getInteger(lastKey) else null)
                    }
                    onFormat(visibleDimension(MediaFormat.KEY_WIDTH, "crop-left", "crop-right"),
                        visibleDimension(MediaFormat.KEY_HEIGHT, "crop-top", "crop-bottom"))
                }
                else -> if (index >= 0) {
                    val render = info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG == 0 &&
                        (info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM == 0 || info.size > 0)
                    if (render) {
                        decodedFrames++
                        progress.decoded()
                    }
                    decoder.releaseOutputBuffer(index, render)
                }
            }
        }
    }

    private fun configure(packet: DesktopVideoPacket, key: Triple<Int, Int, Int>) {
        releaseCodec()
        val mime = if (packet.codec == 1) MediaFormat.MIMETYPE_VIDEO_AVC else MediaFormat.MIMETYPE_VIDEO_HEVC
        val format = MediaFormat.createVideoFormat(mime, packet.width, packet.height).apply {
            setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, DesktopVideoProtocol.MAX_ACCESS_UNIT_SIZE)
            if (Build.VERSION.SDK_INT >= 30) setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
        }
        val decoder = MediaCodec.createDecoderByType(mime)
        try {
            decoder.configure(format, surface, null, 0)
            decoder.setOnFrameRenderedListener({ source, _, nanoTime ->
                if (codec === source) {
                    presentedFrames.incrementAndGet()
                    progress.presented(nanoTime)
                    onPresented()
                }
            }, Handler(renderThread.looper))
            decoder.start()
            decoderName = decoder.name
            decoderMode = if (Build.VERSION.SDK_INT >= 29) {
                if (decoder.codecInfo.isHardwareAccelerated) "hardware" else "software"
            } else "unknown"
            codec = decoder
            formatKey = key
            onFormat(packet.width, packet.height)
        } catch (error: Exception) {
            decoder.release()
            throw error
        }
    }

    private fun releaseCodec() {
        val previous = codec
        codec = null
        formatKey = null
        progress.reset()
        previous?.let { decoder ->
            try { decoder.stop() } finally { decoder.release() }
        }
    }

    @Synchronized override fun close() {
        if (closed) return
        closed = true
        outputHandler.removeCallbacks(outputDrain)
        try { releaseCodec() } finally { renderThread.quitSafely() }
    }
}
