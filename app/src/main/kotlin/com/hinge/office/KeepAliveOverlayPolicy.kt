package com.hinge.office

internal fun shouldShowKeepAliveOverlay(
    enabled: Boolean,
    permissionGranted: Boolean,
    activityForeground: Boolean,
    lowPowerStandby: Boolean,
): Boolean = enabled && permissionGranted && !activityForeground && lowPowerStandby
