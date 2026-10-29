import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

/// Desktop gestures share one intent lock, so a pinch never sends a wheel event.
class DesktopGestureSurface extends StatefulWidget {
  final Widget child;
  final double aspect, sensitivity;
  final bool enabled, direct, zoomOnly, dragging;
  final ValueChanged<Offset> onAbsolute, onRelative;
  final ValueChanged<bool> onDrag;
  final ValueChanged<bool> onClick;
  final ValueChanged<int> onScroll;
  const DesktopGestureSurface({
    super.key,
    required this.child,
    required this.aspect,
    required this.enabled,
    required this.direct,
    required this.zoomOnly,
    required this.dragging,
    required this.sensitivity,
    required this.onAbsolute,
    required this.onRelative,
    required this.onDrag,
    required this.onClick,
    required this.onScroll,
  });
  @override
  State<DesktopGestureSurface> createState() => _DesktopGestureSurfaceState();
}

class _DesktopGestureSurfaceState extends State<DesktopGestureSurface> {
  final _points = <int, Offset>{};
  Timer? _hold;
  Offset _start = Offset.zero, _pan = Offset.zero, _basePan = Offset.zero;
  Offset _baseCenter = Offset.zero, _lastCenter = Offset.zero;
  double _scale = 1, _baseScale = 1, _baseDistance = 0;
  Size _size = Size.zero;
  bool _moved = false, _multi = false, _ownDrag = false;
  String? _intent;
  DateTime? _lastTap;
  Offset? _lastTapPosition;
  Offset _fraction = Offset.zero;
  double _wheelRemainder = 0;

  Offset? _map(Offset point) {
    final scene = (point - _pan) / _scale;
    final width = _size.aspectRatio > widget.aspect
        ? _size.height * widget.aspect
        : _size.width;
    final height = width / widget.aspect;
    final rect = Rect.fromLTWH(
      (_size.width - width) / 2,
      (_size.height - height) / 2,
      width,
      height,
    );
    if (!rect.contains(scene)) return null;
    return Offset(
      (scene.dx - rect.left) / (width - 1).clamp(1, double.infinity),
      (scene.dy - rect.top) / (height - 1).clamp(1, double.infinity),
    );
  }

  void _absolute(Offset point) {
    final normalized = _map(point);
    if (normalized != null) {
      widget.onAbsolute(
        Offset(normalized.dx.clamp(0, 1), normalized.dy.clamp(0, 1)),
      );
    }
  }

  void _beginDrag() {
    if (!mounted || !widget.enabled || widget.zoomOnly || _multi) return;
    _ownDrag = true;
    if (widget.direct) _absolute(_start);
    widget.onDrag(true);
    unawaited(HapticFeedback.selectionClick());
  }

  void _reset({bool duringUpdate = false}) {
    _hold?.cancel();
    if (_ownDrag) {
      if (duringUpdate) {
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (mounted && widget.dragging) widget.onDrag(false);
        });
      } else {
        widget.onDrag(false);
      }
    }
    _ownDrag = false;
    _points.clear();
    _multi = false;
    _lastTap = null;
    _lastTapPosition = null;
    _fraction = Offset.zero;
    _wheelRemainder = 0;
  }

  @override
  void didUpdateWidget(covariant DesktopGestureSurface oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.enabled != widget.enabled ||
        oldWidget.direct != widget.direct ||
        oldWidget.zoomOnly != widget.zoomOnly) {
      _reset(duringUpdate: true);
    }
    if (oldWidget.aspect != widget.aspect) {
      _scale = 1;
      _pan = Offset.zero;
      _reset(duringUpdate: true);
    }
  }

  void _down(PointerDownEvent e) {
    if (!widget.enabled) return;
    if (!widget.zoomOnly && _points.isEmpty && _map(e.localPosition) == null) {
      return;
    }
    _points[e.pointer] = e.localPosition;
    if (_points.length == 1) {
      _start = e.localPosition;
      _moved = false;
      _multi = false;
      _intent = null;
      if (widget.zoomOnly) return;
      final second =
          _lastTap != null &&
          _lastTapPosition != null &&
          DateTime.now().difference(_lastTap!) <
              const Duration(milliseconds: 330) &&
          (e.localPosition - _lastTapPosition!).distance < 32;
      if (second && !widget.dragging) {
        _beginDrag();
        _lastTap = null;
      } else if (!widget.dragging) {
        _hold = Timer(const Duration(milliseconds: 450), () {
          if (_points.length == 1 && !_moved) _beginDrag();
        });
      }
    } else if (_points.length == 2) {
      _hold?.cancel();
      if (_ownDrag) widget.onDrag(false);
      _ownDrag = false;
      _multi = true;
      _intent = null;
      _lastTap = null;
      final pair = _points.values.toList();
      _baseCenter = _lastCenter = (pair[0] + pair[1]) / 2;
      _baseDistance = (pair[0] - pair[1]).distance;
      _baseScale = _scale;
      _basePan = _pan;
    }
  }

  void _move(PointerMoveEvent e) {
    final previous = _points[e.pointer];
    if (previous == null || !widget.enabled) return;
    _points[e.pointer] = e.localPosition;
    if (_points.length >= 2) {
      final pair = _points.values.take(2).toList();
      final center = (pair[0] + pair[1]) / 2;
      final distance = (pair[0] - pair[1]).distance;
      if (_intent == null) {
        if ((distance - _baseDistance).abs() > 10) {
          _intent = 'zoom';
        } else if ((center - _baseCenter).distance > 12) {
          _intent = widget.zoomOnly ? 'zoom' : 'scroll';
        } else {
          return;
        }
      }
      if (_intent == 'zoom') {
        final next =
            (_baseScale * distance / _baseDistance.clamp(1, double.infinity))
                .clamp(1.0, 4.0);
        final pan = center - (_baseCenter - _basePan) * (next / _baseScale);
        setState(() {
          _scale = next;
          _pan = Offset(
            pan.dx.clamp(_size.width * (1 - next), 0),
            pan.dy.clamp(_size.height * (1 - next), 0),
          );
        });
      } else {
        _wheelRemainder +=
            -(center.dy - _lastCenter.dy) * 2 * widget.sensitivity;
        final wheel = _wheelRemainder.truncate();
        if (wheel != 0) {
          widget.onScroll(wheel);
          _wheelRemainder -= wheel;
        }
      }
      _lastCenter = center;
      return;
    }
    if (_multi) return;
    final delta = e.localPosition - previous;
    if ((e.localPosition - _start).distance > 8) {
      _moved = true;
      _hold?.cancel();
    }
    if (widget.zoomOnly) {
      setState(
        () => _pan = Offset(
          (_pan.dx + delta.dx).clamp(_size.width * (1 - _scale), 0),
          (_pan.dy + delta.dy).clamp(_size.height * (1 - _scale), 0),
        ),
      );
    } else if (widget.direct) {
      if (_moved && !_ownDrag && !widget.dragging) _beginDrag();
      if (_moved || _ownDrag || widget.dragging) _absolute(e.localPosition);
    } else {
      _fraction += delta * widget.sensitivity;
      final move = Offset(
        _fraction.dx.truncateToDouble(),
        _fraction.dy.truncateToDouble(),
      );
      if (move != Offset.zero) {
        widget.onRelative(move);
        _fraction -= move;
      }
    }
  }

  void _up(PointerEvent e) {
    if (!_points.containsKey(e.pointer)) return;
    _hold?.cancel();
    _points.remove(e.pointer);
    if (_points.isNotEmpty) return;
    if (!widget.zoomOnly) {
      if (_ownDrag) {
        widget.onDrag(false);
        _ownDrag = false;
      } else if (_multi) {
        if (_intent == null) widget.onClick(true);
      } else if (!_moved && !widget.dragging) {
        if (widget.direct) _absolute(e.localPosition);
        widget.onClick(false);
        _lastTap = DateTime.now();
        _lastTapPosition = e.localPosition;
      }
    }
    _multi = false;
    _intent = null;
  }

  @override
  void dispose() {
    _hold?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final size = constraints.biggest;
      if (_size != size) {
        _hold?.cancel();
        _points.clear();
        _ownDrag = false;
        _multi = false;
        _size = size;
        _scale = 1;
        _pan = Offset.zero;
      }
      return Listener(
        behavior: HitTestBehavior.opaque,
        onPointerDown: _down,
        onPointerMove: _move,
        onPointerUp: _up,
        onPointerCancel: (_) => _reset(),
        child: ClipRect(
          child: Transform(
            transform: Matrix4.identity()
              ..translateByDouble(_pan.dx, _pan.dy, 0, 1)
              ..scaleByDouble(_scale, _scale, 1, 1),
            child: widget.child,
          ),
        ),
      );
    },
  );
}
