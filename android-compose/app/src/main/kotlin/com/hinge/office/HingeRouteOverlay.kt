package com.hinge.office

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.EnterTransition
import androidx.compose.animation.ExitTransition
import androidx.compose.animation.core.CubicBezierEasing
import androidx.compose.animation.core.Easing
import androidx.compose.animation.core.Transition
import androidx.compose.animation.core.tween
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.key
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.input.pointer.PointerEventPass
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.semantics.clearAndSetSemantics

internal val HingeSlideEasing = CubicBezierEasing(.215f, .61f, .355f, 1f)
private val FlutterReverseCurve = CubicBezierEasing(.55f, .055f, .675f, .19f)
// Flutter runs this curve from 1 to 0. Compose advances exit distance from 0 to 1.
internal val HingeReturnEasing = Easing { progress -> 1f - FlutterReverseCurve.transform(1f - progress) }

internal fun popHingeRoute(stack: List<String>, expected: String): List<String> =
    if (stack.lastOrNull() == expected) stack.dropLast(1) else stack

private fun Modifier.routeTouchLayer(block: Boolean) = pointerInput(block) {
    awaitPointerEventScope {
        while (true) {
            val event = awaitPointerEvent(PointerEventPass.Initial)
            if (block) event.changes.forEach { it.consume() }
        }
    }
}

@Composable
internal fun HingeRouteOverlay(
    transition: Transition<List<String>>,
    rtl: Boolean,
    reducedMotion: Boolean,
    content: @Composable (route: String, active: Boolean) -> Unit,
) {
    val moving = transition.currentState != transition.targetState || transition.isRunning
    val direction = if (rtl) -1 else 1
    val entering = if (reducedMotion) EnterTransition.None
        else slideInHorizontally(tween(320, easing = HingeSlideEasing)) { direction * it }
    val leaving = if (reducedMotion) ExitTransition.None
        else slideOutHorizontally(tween(220, easing = HingeReturnEasing)) { direction * it }
    Box(Modifier.fillMaxSize().clipToBounds()) {
        for (route in (transition.currentState + transition.targetState).distinct()) key(route) {
            transition.AnimatedVisibility(
                visible = { stack -> route in stack },
                enter = entering,
                exit = leaving,
                modifier = Modifier.fillMaxSize(),
            ) {
                val active = route == transition.targetState.lastOrNull() && !moving
                Box(Modifier.fillMaxSize()
                    .background(MaterialTheme.colorScheme.surface)
                    .routeTouchLayer(block = false)
                    .then(if (active) Modifier else Modifier.clearAndSetSemantics {})) {
                    content(route, active)
                }
            }
        }
    }
    if (moving) Box(Modifier.fillMaxSize().routeTouchLayer(block = true))
}
