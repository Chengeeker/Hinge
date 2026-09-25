import 'package:flutter/material.dart';

/// Builds app feedback without covering the custom floating navigation capsule.
SnackBar buildHingeMessageSnackBar(
  String message, {
  required bool floatingCapsuleVisible,
  required double capsuleBottomMargin,
}) {
  return SnackBar(
    content: Text(message),
    behavior: floatingCapsuleVisible
        ? SnackBarBehavior.floating
        : SnackBarBehavior.fixed,
    margin: floatingCapsuleVisible
        ? EdgeInsets.fromLTRB(16, 0, 16, capsuleBottomMargin + 64 + 12)
        : null,
  );
}
