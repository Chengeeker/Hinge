package com.hinge.office

import android.content.Context
import android.os.PowerManager
import android.os.SystemClock
import androidx.test.core.app.ApplicationProvider
import java.io.ByteArrayOutputStream
import java.time.Duration
import java.util.concurrent.ExecutorService
import java.util.concurrent.ScheduledFuture
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.RobolectricTestRunner
import org.robolectric.Shadows.shadowOf
import org.robolectric.annotation.Config
import org.robolectric.shadows.ShadowSystemClock
import org.robolectric.util.ReflectionHelpers

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [28, 35], application = android.app.Application::class)
class BackgroundConnectionTest {
    private lateinit var service: HingeForegroundService
    private lateinit var broker: HingeNativeConnectionBroker
    private lateinit var power: PowerManager
    private var activeNotifications = 0
    private val connections = mutableListOf<Any>()

    @Before fun setUp() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        power = context.getSystemService(PowerManager::class.java)
        service = Robolectric.buildService(HingeForegroundService::class.java).get()
        broker = HingeNativeConnectionBroker(context) { activeNotifications++ }
        set(service, "nativeConnectionBroker", broker)
        ShadowSystemClock.advanceBy(Duration.ofSeconds(1))
    }

    @After fun tearDown() {
        connections.forEach { cancelHeartbeat(it) }
        service.onDestroy()
        listOf("executor", "bridgeWriteExecutor", "scheduler").forEach {
            get<ExecutorService>(broker, it).shutdownNow()
        }
    }

    @Test fun screenOffKeepsResponsiveSessionAndWakeLockBeyondIdleWindow() {
        shadowOf(power).setIsInteractive(false)
        val connection = connection()
        acquireCpuWakeLock(0L)
        ShadowSystemClock.advanceBy(Duration.ofMinutes(10))
        call(service, "evaluateLowPowerStandby")
        assertFalse(get<Boolean>(service, "lowPowerStandby"))
        assertTrue(get<PowerManager.WakeLock>(service, "cpuWakeLock").isHeld)
        assertTrue(broker.hasActiveWork())

        // A truly unresponsive peer must not pin a wake lock forever.
        set(connection, "state", "suspended")
        assertFalse(broker.hasActiveWork())
        ShadowSystemClock.advanceBy(Duration.ofSeconds(91))
        call(service, "evaluateLowPowerStandby")
        assertTrue(get<Boolean>(service, "lowPowerStandby"))
        assertNull(get<PowerManager.WakeLock?>(service, "cpuWakeLock"))
    }

    @Test fun idleWithoutSessionCanStillSleepButActiveSendCannot() {
        shadowOf(power).setIsInteractive(false)
        val tasks = get<MutableSet<String>>(broker, "activeTasks")
        tasks.add("sending")
        call(service, "evaluateLowPowerStandby")
        assertFalse(get<Boolean>(service, "lowPowerStandby"))
        tasks.clear()
        ShadowSystemClock.advanceBy(Duration.ofSeconds(91))
        call(service, "evaluateLowPowerStandby")
        assertTrue(get<Boolean>(service, "lowPowerStandby"))
    }

    @Test fun delayedFirstTickAfterSleepProbesBeforeDisconnecting() {
        val connection = connection()
        ShadowSystemClock.advanceBy(Duration.ofMinutes(10))
        tick(connection)
        assertFalse(get<Boolean>(connection, "closed"))
        assertEquals("suspended", get<String>(connection, "state"))
        assertTrue(get<ByteArrayOutputStream>(connection, "output").size() > 0)
        ShadowSystemClock.advanceBy(Duration.ofSeconds(16))
        tick(connection)
        assertTrue(get<Boolean>(connection, "closed"))
    }

    @Test fun screenOnAfterShortSleepAlsoGetsRecoveryProbe() {
        val connection = connection()
        shadowOf(power).setIsInteractive(false)
        ShadowSystemClock.advanceBy(Duration.ofSeconds(40))
        tick(connection)
        shadowOf(power).setIsInteractive(true)
        ShadowSystemClock.advanceBy(Duration.ofSeconds(20))
        tick(connection)
        assertFalse(get<Boolean>(connection, "closed"))
        assertEquals("suspended", get<String>(connection, "state"))
    }

    @Test fun continuouslyScheduledForegroundTimeoutIsNotHidden() {
        val connection = connection()
        repeat(10) {
            ShadowSystemClock.advanceBy(Duration.ofSeconds(5))
            tick(connection)
        }
        assertTrue(get<Boolean>(connection, "closed"))
    }

    @Test fun realInboundFrameCompletesRecoveryAndKeepsSessionUsable() {
        val connection = connection()
        ShadowSystemClock.advanceBy(Duration.ofMinutes(10))
        tick(connection)
        receivePong(connection)
        ShadowSystemClock.advanceBy(Duration.ofSeconds(16))
        tick(connection)
        assertEquals("connected", get<String>(connection, "state"))
        assertFalse(get<Boolean>(connection, "closed"))
    }

    @Test fun returningToForegroundHidesOldReadyStateUntilPeerResponds() {
        val connection = connection()
        assertEquals("connected", broker.connectionState("test-0"))
        service.setActivityForeground(true)
        assertEquals("suspended", broker.connectionState("test-0"))
        val probe = get<Any>(connection, "foregroundProbe")
        receivePong(connection)
        expireProbe(connection, probe)
        assertEquals("connected", broker.connectionState("test-0"))
    }

    @Test fun foregroundProbeExpiryRemovesUnresponsiveSession() {
        val connection = connection()
        broker.revalidateForegroundSessions()
        val firstProbe = get<Any>(connection, "foregroundProbe")
        broker.revalidateForegroundSessions()
        expireProbe(connection, firstProbe)
        assertEquals("suspended", broker.connectionState("test-0"))
        expireProbe(connection, get(connection, "foregroundProbe"))
        assertNull(broker.connectionState("test-0"))
    }

    @Test fun repeatedAdbWakeDoesNotPostponeProbeTimeout() {
        val connection = connection()
        broker.revalidateForegroundSessions()
        val probe = get<Any>(connection, "foregroundProbe")
        ReflectionHelpers.callInstanceMethod<Unit>(connection, "probeOnForeground",
            ReflectionHelpers.ClassParameter.from(Boolean::class.javaPrimitiveType!!, false))
        assertSame(probe, get<Any>(connection, "foregroundProbe"))
        expireProbe(connection, probe)
        assertNull(broker.connectionState("test-0"))
    }

    @Test fun ordinaryAppCannotRequestShellWake() {
        val provider = Robolectric.buildContentProvider(HingeDiagnosticsProvider::class.java).create().get()
        assertThrows(SecurityException::class.java) { provider.call("adb_wake", "invalid", null) }
    }

    private fun receivePong(connection: Any) {
        val frameType = Class.forName("com.hinge.office.NativeProtocolFrame")
        val frame = frameType.declaredConstructors.single().apply { isAccessible = true }
            .newInstance(1, 0x0013, ByteArray(16), 0L, ByteArray(16), ByteArray(0))
        set(connection, "lastInboundAt", SystemClock.elapsedRealtime())
        connection.javaClass.getDeclaredMethod("handleFrame", frameType)
            .apply { isAccessible = true }.invoke(connection, frame)
    }

    private fun expireProbe(connection: Any, probe: Any) {
        ReflectionHelpers.callInstanceMethod<Unit>(connection, "expireForegroundProbe",
            ReflectionHelpers.ClassParameter.from(Any::class.java, probe))
    }

    @Test fun newlyAuthenticatedSessionRequestsServiceWakeAndRestartsMissingHeartbeat() {
        val connection = connection()
        broker.enterLowPowerStandby()
        broker.javaClass.getDeclaredMethod("onConnectionReady", connection.javaClass)
            .apply { isAccessible = true }.invoke(broker, connection)
        assertEquals(1, activeNotifications)
        assertNull(get<ScheduledFuture<*>?>(connection, "heartbeatFuture"))
        broker.exitLowPowerStandby()
        assertNotNull(get<ScheduledFuture<*>?>(connection, "heartbeatFuture"))
    }

    @Test fun serviceWakeRestoresLocksAndClearsBothStandbyStates() {
        set(service, "lowPowerStandby", true)
        broker.enterLowPowerStandby()
        ReflectionHelpers.callInstanceMethod<Any?>(service, "activateConnectionWindow",
            ReflectionHelpers.ClassParameter.from(String::class.java, "session_active"),
            ReflectionHelpers.ClassParameter.from(Long::class.javaPrimitiveType!!, 0L))
        assertFalse(get<Boolean>(service, "lowPowerStandby"))
        assertFalse(get<Boolean>(broker, "lowPowerStandby"))
        assertTrue(get<PowerManager.WakeLock>(service, "cpuWakeLock").isHeld)
        assertNotNull(get<Any?>(service, "discoveryBeaconRunnable"))
    }

    @Test fun repeatedBleWakeDoesNotExtendTheLockAndActiveWorkCanTakeOver() {
        acquireCpuWakeLock(30_000L)
        val timedLock = get<PowerManager.WakeLock>(service, "cpuWakeLock")
        assertTrue(timedLock.isHeld)
        assertTrue(get<Boolean>(service, "cpuWakeLockTimed"))
        acquireCpuWakeLock(30_000L)
        assertSame(timedLock, get<PowerManager.WakeLock>(service, "cpuWakeLock"))
        acquireCpuWakeLock(0L)
        assertFalse(timedLock.isHeld)
        assertTrue(get<PowerManager.WakeLock>(service, "cpuWakeLock").isHeld)
        assertFalse(get<Boolean>(service, "cpuWakeLockTimed"))
    }

    @Test fun authenticationIsProtectedButClosedSocketsAreNotActiveWork() {
        val connection = connection()
        set(connection, "isReady", false)
        set(connection, "state", "authenticating")
        assertTrue(broker.hasActiveWork())
        set(connection, "closed", true)
        assertFalse(broker.hasActiveWork())
    }

    private fun connection(): Any {
        val type = Class.forName("com.hinge.office.HingeNativeConnectionBroker\$NativeConnection")
        val connection = type.declaredConstructors.single { it.parameterCount == 9 }
            .apply { isAccessible = true }
            .newInstance(broker, "test-${connections.size}", null, "127.0.0.1", 52831, false, "", false, "")
        set(connection, "isReady", true)
        set(connection, "state", "connected")
        set(connection, "output", ByteArrayOutputStream())
        connections.add(connection)
        get<MutableMap<String, Any>>(broker, "connections")["test-${connections.size - 1}"] = connection
        return connection
    }

    private fun tick(connection: Any) {
        call(connection, "heartbeat")
        cancelHeartbeat(connection)
    }
    private fun cancelHeartbeat(connection: Any) {
        get<ScheduledFuture<*>?>(connection, "heartbeatFuture")?.cancel(false)
    }
    private fun call(target: Any, method: String) = ReflectionHelpers.callInstanceMethod<Any?>(target, method)
    private fun acquireCpuWakeLock(timeoutMs: Long) {
        ReflectionHelpers.callInstanceMethod<Unit>(service, "acquireCpuWakeLock",
            ReflectionHelpers.ClassParameter.from(Long::class.javaPrimitiveType!!, timeoutMs))
    }
    private fun set(target: Any, name: String, value: Any) = ReflectionHelpers.setField(target, name, value)
    private fun <T> get(target: Any, name: String): T = ReflectionHelpers.getField(target, name)
}
