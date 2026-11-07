package com.hinge.office

import org.junit.Assert.assertEquals
import org.junit.Test

class ConnectedDevicesTest {
    private fun session(id: String, socket: String, state: String = "connected", name: String = "Desktop") =
        mapOf("deviceId" to id, "connectionId" to socket, "state" to state, "name" to name)

    @Test fun multipleSocketsCountAsOneDeviceAndKeepAnExistingSession() {
        val first = session("desktop-1", "first")
        val sessions = listOf(first, session("desktop-1", "second"), session("desktop-1", "third"))
        assertEquals(listOf(first), connectedDeviceRows(sessions))
        assertEquals(listOf(sessions[1]), connectedDeviceRows(sessions.drop(1)))
        assertEquals("Desktop", connectedDeviceLabel(first, connectedDeviceRows(sessions)))
    }

    @Test fun connectingSuspendedAndUnidentifiedRowsDoNotCountAsOnline() {
        val ready = session("desktop-1", "live")
        assertEquals(
            listOf(ready),
            connectedDeviceRows(listOf(session("desktop-1", "old", "disconnected"),
                session("desktop-2", "sleep", "suspended"), session("desktop-3", "pending", "connecting"),
                session("", "anonymous"), ready))
        )
    }

    @Test fun twoDifferentDeviceIdsWithTheSameNameRemainSeparate() {
        val first = session("11111111-abcd", "first")
        val second = session("22222222-abcd", "second", name = "desktop")
        val devices = connectedDeviceRows(listOf(first, second))
        assertEquals(2, devices.size)
        assertEquals("Desktop · 11111111", connectedDeviceLabel(first, devices))
        assertEquals("desktop · 22222222", connectedDeviceLabel(second, devices))
    }

    @Test fun manualDisconnectRejectsAutomaticAndAlreadyPendingConnections() {
        for (outbound in listOf(false, true)) {
            assertEquals(false, canResumeManuallyDisconnectedDevice(outbound, false, false, 11, 10))
            assertEquals(false, canResumeManuallyDisconnectedDevice(outbound, true, true, 9, 10))
            assertEquals(false, canResumeManuallyDisconnectedDevice(outbound, !outbound, outbound, 11, 10))
            assertEquals(true, canResumeManuallyDisconnectedDevice(outbound, outbound, !outbound, 11, 10))
        }
    }
}
