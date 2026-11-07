package com.hinge.office

import android.content.Context
import android.graphics.Bitmap
import android.opengl.EGL14
import android.opengl.EGLConfig
import android.opengl.GLES20.*
import android.opengl.GLUtils
import java.io.Closeable
import java.nio.ByteBuffer
import java.nio.ByteOrder

internal class GlassRenderer(context: Context) : Closeable {
    private val display = EGL14.eglGetDisplay(EGL14.EGL_DEFAULT_DISPLAY)
    private var eglContext = EGL14.EGL_NO_CONTEXT
    private var surface = EGL14.EGL_NO_SURFACE
    private var config: EGLConfig? = null
    var maxSurfaceSize = 1024
        private set
    private val textures = IntArray(14)
    private val framebuffer = IntArray(1)
    private val programs = mutableListOf<Program>()
    private val vertices = ByteBuffer.allocateDirect(8 * 4).order(ByteOrder.nativeOrder())
        .asFloatBuffer().apply { put(floatArrayOf(-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f)); rewind() }
    private var width = 0
    private var height = 0
    private var pixels = ByteBuffer.allocateDirect(0)
    private var colors = IntArray(0)
    private var page: Bitmap? = null
    private var unselected: Bitmap? = null
    private var selected: Bitmap? = null
    private var blurredDensity = 0f
    private var shellShape: GlassShape? = null
    private var shellDark = false
    private var frostReady = false
    private var shadowShape: GlassShape? = null

    init {
        try {
            check(EGL14.eglInitialize(display, IntArray(2), 0, IntArray(2), 0))
            val configs = arrayOfNulls<EGLConfig>(1)
            val count = IntArray(1)
            check(EGL14.eglChooseConfig(display, intArrayOf(
                EGL14.EGL_RED_SIZE, 8, EGL14.EGL_GREEN_SIZE, 8, EGL14.EGL_BLUE_SIZE, 8,
                EGL14.EGL_ALPHA_SIZE, 8, EGL14.EGL_RENDERABLE_TYPE, EGL14.EGL_OPENGL_ES2_BIT,
                EGL14.EGL_SURFACE_TYPE, EGL14.EGL_PBUFFER_BIT, EGL14.EGL_NONE,
            ), 0, configs, 0, 1, count, 0) && count[0] > 0)
            config = configs[0]
            eglContext = EGL14.eglCreateContext(display, configs[0], EGL14.EGL_NO_CONTEXT,
                intArrayOf(EGL14.EGL_CONTEXT_CLIENT_VERSION, 2, EGL14.EGL_NONE), 0)
            surface = EGL14.eglCreatePbufferSurface(display, configs[0],
                intArrayOf(EGL14.EGL_WIDTH, 1024, EGL14.EGL_HEIGHT, 512, EGL14.EGL_NONE), 0)
            check(EGL14.eglMakeCurrent(display, surface, surface, eglContext))
            val limit = IntArray(1)
            glGetIntegerv(GL_MAX_TEXTURE_SIZE, limit, 0)
            maxSurfaceSize = minOf(4096, limit[0])
            for (attribute in intArrayOf(EGL14.EGL_MAX_PBUFFER_WIDTH, EGL14.EGL_MAX_PBUFFER_HEIGHT)) {
                check(EGL14.eglGetConfigAttrib(display, config, attribute, limit, 0))
                maxSurfaceSize = minOf(maxSurfaceSize, limit[0])
            }

            fun asset(name: String) = context.assets.open("glass/$name.glsl")
                .bufferedReader().use { it.readText().removePrefix("\uFEFF") }
            val capsule = asset("capsule")
            programs += Program(asset("gaussian_blur"))
            programs += Program(capsule + "\n" + asset("shell"))
            programs += Program(capsule + "\n" + asset("tab_scene"))
            programs += Program(capsule + "\n" + asset("tab_lens"))
            programs += Program(asset("flutter_geometry"))
            programs += Program(asset("flutter_render"))
            programs += Program(capsule + "\n" + asset("shell_backdrop"))
            programs += Program(capsule + "\n" + asset("shell_blur"))
            glGenTextures(textures.size, textures, 0)
            glGenFramebuffers(1, framebuffer, 0)
            for (id in textures) {
                glBindTexture(GL_TEXTURE_2D, id)
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR)
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR)
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE)
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE)
            }
        } catch (error: Exception) {
            close()
            throw error
        }
    }

    fun render(
        backdrop: Bitmap, inactiveArt: Bitmap, activeArt: Bitmap,
        density: Float, barWidth: Float, dark: Boolean, motion: GlassMotion,
    ): Bitmap {
        resize(backdrop.width, backdrop.height)
        val geometry = glassGeometry(barWidth, motion)
        if (backdrop !== page || density != blurredDensity) {
            upload(PAGE, backdrop)
            page = backdrop
            if (density != blurredDensity) shadowShape = null
            blurredDensity = density
            shellShape = null
        }
        if (inactiveArt !== unselected) {
            upload(UNSELECTED, inactiveArt)
            unselected = inactiveArt
        }
        if (activeArt !== selected) {
            upload(SELECTED, activeArt)
            selected = activeArt
        }

        val darkValue = if (dark) 1f else 0f
        if (shellShape != geometry.bar || shellDark != dark) {
            drawGeometry(density, geometry.bar, dark, false, 1f)
            if (!dark && shadowShape != geometry.bar) {
                blur(GEOMETRY, ELEVATION, 3.04034f * density / 2f)
                blur(GEOMETRY, CONTACT, 1.135085f * density / 2f)
                shadowShape = geometry.bar
            }
            pass(programs[6], SHADOW_BACKDROP) {
                canvas(density)
                bind("uPage", 0, PAGE)
                bind("uGeometry", 1, GEOMETRY)
                bind("uElevation", 2, ELEVATION)
                bind("uContact", 3, CONTACT)
                scalar("uDark", darkValue)
            }
            blur(SHADOW_BACKDROP, BLURRED_PAGE, .75f * density)
            pass(programs[7], FILTERED_BACKDROP) {
                canvas(density)
                bind("uBackdrop", 0, SHADOW_BACKDROP)
                bind("uBlurred", 1, BLURRED_PAGE)
                shape("uBar", geometry.bar)
            }
            optics(FILTERED_BACKDROP, density, geometry.bar, dark, false, 0f)
            pass(programs[1], SHELL) {
                canvas(density)
                bind("uPrepared", 0, FILTERED_BACKDROP)
                bind("uOptical", 1, OPTICAL)
                bind("uBackdrop", 2, SHADOW_BACKDROP)
            }
            shellShape = geometry.bar
            shellDark = dark
            frostReady = false
        }
        if (geometry.restOpacity > GLASS_VISIBLE_PRESSURE && !frostReady) {
            blur(SHELL, REST_BLUR, 3f * density)
            frostReady = true
        }

        pass(programs[2], SCENE) {
            canvas(density)
            bind("uShell", 0, SHELL)
            bind("uRestBlur", 1, REST_BLUR)
            bind("uUnselected", 2, UNSELECTED)
            bind("uSelected", 3, SELECTED)
            shape("uBar", geometry.bar)
            shape("uLens", geometry.lens)
            scalar("uRestOpacity", geometry.restOpacity)
            scalar("uDark", darkValue)
        }
        if (geometry.pressure > GLASS_VISIBLE_PRESSURE) {
            drawGeometry(density, geometry.lens, dark, true, geometry.pressure)
            optics(SCENE, density, geometry.lens, dark, true, geometry.pressure)
        }
        pass(programs[3], null) {
            canvas(density)
            bind("uPage", 0, PAGE)
            bind("uScene", 1, SCENE)
            bind("uOptical", 2, OPTICAL)
            bind("uShell", 3, SHELL)
            shape("uBar", geometry.bar)
            scalar("uPressure", geometry.pressure)
            scalar("uDark", darkValue)
        }

        return readBitmap()
    }

    fun blurBackdrop(backdrop: Bitmap, sigma: Float): Bitmap {
        resize(backdrop.width, backdrop.height)
        upload(PAGE, backdrop)
        blur(PAGE, null, sigma / 2f)
        return readBitmap()
    }

    private fun readBitmap(): Bitmap {
        // All passes use row zero as the top, matching the uploaded bitmaps.
        pixels.clear()
        glReadPixels(0, 0, width, height, GL_RGBA, GL_UNSIGNED_BYTE, pixels)
        check(glGetError() == GL_NO_ERROR) { "玻璃画面读取失败" }
        pixels.rewind()
        for (i in colors.indices) {
            val rgba = pixels.int
            val alpha = rgba ushr 24
            // Bitmap.createBitmap accepts straight ARGB; avoid premultiplying
            // the already-premultiplied antialias and shadow a second time.
            colors[i] = when (alpha) {
                0 -> 0
                255 -> (alpha shl 24) or ((rgba and 255) shl 16) or
                    (rgba and 0xff00) or ((rgba ushr 16) and 255)
                else -> {
                    val r = ((rgba and 255) * 255 + alpha / 2) / alpha
                    val g = (((rgba ushr 8) and 255) * 255 + alpha / 2) / alpha
                    val b = (((rgba ushr 16) and 255) * 255 + alpha / 2) / alpha
                    (alpha shl 24) or (r shl 16) or (g shl 8) or b
                }
            }
        }
        return Bitmap.createBitmap(colors, width, height, Bitmap.Config.ARGB_8888)
    }

    private fun drawGeometry(density: Float, rect: GlassShape, dark: Boolean, indicator: Boolean, pressure: Float) {
        val settings = glassOptics(density, dark, indicator, pressure)
        val cx = rect.centerX * density
        val cy = rect.centerY * density
        pass(programs[4], GEOMETRY) {
            pair("uSize", width.toFloat(), height.toFloat())
            quad("uTransform", cx, cy, 1f, 1f)
            quad("uOpticalProps", settings.index, settings.dispersion, settings.thickness, 0f)
            pair("uShapeSettings", 1f, density)
            floats("uShapeData", floatArrayOf(3f, cx, cy, rect.width * density,
                rect.height * density, 9999f * density, 9999f * density))
            scalar("uNativeEdge", if (indicator && !dark) 1f else 0f)
        }
    }

    private fun optics(source: Int, density: Float, rect: GlassShape, dark: Boolean, indicator: Boolean, pressure: Float) {
        val settings = glassOptics(density, dark, indicator, pressure)
        val cx = rect.centerX * density
        val cy = rect.centerY * density
        val w = rect.width * density
        val h = rect.height * density
        pass(programs[5], OPTICAL) {
            bind("uBackgroundTexture", 0, source)
            bind("uGeometryTexture", 1, GEOMETRY)
            pair("uSize", width.toFloat(), height.toFloat())
            pair("uBackdropSize", width.toFloat(), height.toFloat())
            pair("uCropOrigin", 0f, 0f)
            pair("uGeometryOffset", cx - w / 2f, cy - h / 2f)
            pair("uGeometrySize", w, h)
            quad("uGlassColor", settings.tint, settings.tint, settings.tint, settings.tintAlpha)
            quad("uOpticalProps", settings.index, settings.dispersion, settings.thickness, 1f)
            triple("uLightConfig", settings.light, 0f, settings.saturation)
            pair("uLightDirection", -.7071068f, .7071068f)
            pair("uTouchPosition", cx, cy)
            scalar("uTouchIntensity", pressure * .125f)
            quad("uEdgeConfig", 0f, settings.fresnel, density / 3f, 0f)
            triple("uRimConfig", settings.rimShade, settings.rimLight, .2f)
            scalar("uBodyMode", 1f)
            scalar("uWhitenGated", 1f)
            scalar("uPinchStrength", settings.pinch)
        }
    }

    private fun resize(newWidth: Int, newHeight: Int) {
        require(newWidth in 1..maxSurfaceSize && newHeight in 1..maxSurfaceSize)
        if (newWidth == width && newHeight == height) return
        width = newWidth
        height = newHeight
        check(EGL14.eglMakeCurrent(display, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_CONTEXT))
        EGL14.eglDestroySurface(display, surface)
        surface = EGL14.eglCreatePbufferSurface(display, config,
            intArrayOf(EGL14.EGL_WIDTH, width, EGL14.EGL_HEIGHT, height, EGL14.EGL_NONE), 0)
        check(surface != EGL14.EGL_NO_SURFACE && EGL14.eglMakeCurrent(display, surface, surface, eglContext))
        glViewport(0, 0, width, height)
        glActiveTexture(GL_TEXTURE0)
        for (id in textures) {
            glBindTexture(GL_TEXTURE_2D, id)
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, null)
        }
        pixels = ByteBuffer.allocateDirect(width * height * 4).order(ByteOrder.LITTLE_ENDIAN)
        colors = IntArray(width * height)
        page = null
        unselected = null
        selected = null
        shadowShape = null
    }

    private fun upload(slot: Int, bitmap: Bitmap) {
        require(bitmap.width == width && bitmap.height == height)
        glActiveTexture(GL_TEXTURE0)
        glBindTexture(GL_TEXTURE_2D, textures[slot])
        GLUtils.texSubImage2D(GL_TEXTURE_2D, 0, 0, 0, bitmap)
    }

    private fun blur(source: Int, target: Int?, stride: Float) {
        pass(programs[0], BLUR_WORK) {
            bind("uSource", 0, source)
            pair("uSize", width.toFloat(), height.toFloat())
            scalar("uSigma", maxOf(.001f, stride * 2f))
            pair("uStep", 1f / width, 0f)
        }
        pass(programs[0], target) {
            bind("uSource", 0, BLUR_WORK)
            pair("uSize", width.toFloat(), height.toFloat())
            scalar("uSigma", maxOf(.001f, stride * 2f))
            pair("uStep", 0f, 1f / height)
        }
    }

    private inline fun pass(program: Program, target: Int?, uniforms: Program.() -> Unit) {
        glBindFramebuffer(GL_FRAMEBUFFER, if (target == null) 0 else framebuffer[0])
        if (target != null) {
            glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, textures[target], 0)
            check(glCheckFramebufferStatus(GL_FRAMEBUFFER) == GL_FRAMEBUFFER_COMPLETE)
        }
        glUseProgram(program.id)
        program.uniforms()
        glEnableVertexAttribArray(program.position)
        glVertexAttribPointer(program.position, 2, GL_FLOAT, false, 0, vertices)
        glDrawArrays(GL_TRIANGLE_STRIP, 0, 4)
        glDisableVertexAttribArray(program.position)
    }

    private inner class Program(fragment: String) {
        val id: Int
        val position: Int
        private val locations = mutableMapOf<String, Int>()

        init {
            val vertex = shader(GL_VERTEX_SHADER, "attribute vec2 position; void main() { gl_Position = vec4(position, 0.0, 1.0); }")
            val frag = try { shader(GL_FRAGMENT_SHADER, fragment) } catch (error: Exception) {
                glDeleteShader(vertex)
                throw error
            }
            id = glCreateProgram()
            try {
                glAttachShader(id, vertex)
                glAttachShader(id, frag)
                glLinkProgram(id)
                val status = IntArray(1)
                glGetProgramiv(id, GL_LINK_STATUS, status, 0)
                check(status[0] != 0) { glGetProgramInfoLog(id) }
            } catch (error: Exception) {
                glDeleteProgram(id)
                throw error
            } finally {
                glDeleteShader(vertex)
                glDeleteShader(frag)
            }
            position = glGetAttribLocation(id, "position")
        }

        private fun location(name: String) = locations.getOrPut(name) { glGetUniformLocation(id, name) }
        fun bind(name: String, unit: Int, slot: Int) {
            glActiveTexture(GL_TEXTURE0 + unit)
            glBindTexture(GL_TEXTURE_2D, textures[slot])
            glUniform1i(location(name), unit)
        }
        fun canvas(density: Float) {
            pair("uSize", width / density, height / density)
            scalar("uDensity", density)
        }
        fun scalar(name: String, value: Float) = glUniform1f(location(name), value)
        fun pair(name: String, x: Float, y: Float) = glUniform2f(location(name), x, y)
        fun triple(name: String, x: Float, y: Float, z: Float) = glUniform3f(location(name), x, y, z)
        fun quad(name: String, x: Float, y: Float, z: Float, w: Float) = glUniform4f(location(name), x, y, z, w)
        fun floats(name: String, values: FloatArray) = glUniform1fv(location(name), values.size, values, 0)
        fun shape(name: String, rect: GlassShape) =
            glUniform4f(location(name), rect.centerX, rect.centerY, rect.width, rect.height)
    }

    private fun shader(type: Int, source: String): Int {
        val id = glCreateShader(type)
        glShaderSource(id, source)
        glCompileShader(id)
        val status = IntArray(1)
        glGetShaderiv(id, GL_COMPILE_STATUS, status, 0)
        if (status[0] == 0) {
            val message = glGetShaderInfoLog(id)
            glDeleteShader(id)
            error(message)
        }
        return id
    }

    override fun close() {
        if (EGL14.eglGetCurrentContext() == eglContext && eglContext != EGL14.EGL_NO_CONTEXT) {
            programs.forEach { glDeleteProgram(it.id) }
            glDeleteTextures(textures.size, textures, 0)
            glDeleteFramebuffers(1, framebuffer, 0)
            EGL14.eglMakeCurrent(display, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_SURFACE, EGL14.EGL_NO_CONTEXT)
        }
        if (surface != EGL14.EGL_NO_SURFACE) EGL14.eglDestroySurface(display, surface)
        if (eglContext != EGL14.EGL_NO_CONTEXT) EGL14.eglDestroyContext(display, eglContext)
        EGL14.eglReleaseThread()
        EGL14.eglTerminate(display)
    }

    private companion object {
        const val PAGE = 0
        const val BLUR_WORK = 1
        const val BLURRED_PAGE = 2
        const val SHELL = 3
        const val REST_BLUR = 4
        const val UNSELECTED = 5
        const val SELECTED = 6
        const val SCENE = 7
        const val GEOMETRY = 8
        const val OPTICAL = 9
        const val ELEVATION = 10
        const val CONTACT = 11
        const val SHADOW_BACKDROP = 12
        const val FILTERED_BACKDROP = 13
    }
}

