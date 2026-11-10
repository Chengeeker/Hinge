package com.hinge.office

import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Test

class HingeRouteStackTest {
    @Test fun personalizationReturnsToRetainedWorkspaceWithoutPoppingTwice() {
        val routes = listOf("个性化")
        val returned = popHingeRoute(routes, "个性化")
        assertEquals(emptyList<String>(), returned)
        assertSame(returned, popHingeRoute(returned, "个性化"))
    }

    @Test fun staleCallbackCannotPopTheTopOfANestedRouteStack() {
        val routes = listOf("投屏与远控", "手机控制电脑", "电脑桌面")
        assertSame(routes, popHingeRoute(routes, "手机控制电脑"))
        val returned = popHingeRoute(routes, "电脑桌面")
        assertEquals(listOf("投屏与远控", "手机控制电脑"), returned)
        assertSame(returned, popHingeRoute(returned, "电脑桌面"))
    }
}
