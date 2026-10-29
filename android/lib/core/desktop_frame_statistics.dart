import 'dart:collection';
import 'dart:ui';

/// Rebuilt UI frame timings exclude the engine's texture-only redraw fast path.
class DesktopFrameStatistics {
  final _frames = Queue<({int start, int span})>();
  int? _lastReport;

  void reset() {
    _frames.clear();
    _lastReport = null;
  }

  void add(List<FrameTiming> timings, int receivedAtUs) {
    var accepted = false;
    for (final frame in timings) {
      final start = frame.timestampInMicroseconds(FramePhase.vsyncStart);
      final span = frame.totalSpan.inMicroseconds;
      if (span < 0 || (_frames.isNotEmpty && start <= _frames.last.start)) {
        continue;
      }
      _frames.add((start: start, span: span));
      accepted = true;
      while (_frames.length > 512 ||
          (_frames.length > 2 &&
              _frames.elementAt(1).start < start - 1000000)) {
        _frames.removeFirst();
      }
    }
    if (accepted) _lastReport = receivedAtUs;
  }

  ({double p95Ms, int count})? sample(int nowUs) {
    if (_frames.length < 2 ||
        _lastReport == null ||
        nowUs - _lastReport! > 3000000) {
      return null;
    }
    final spans = _frames.map((frame) => frame.span).toList()..sort();
    return (
      p95Ms: spans[(_frames.length * .95).ceil() - 1] / 1000,
      count: _frames.length,
    );
  }
}
