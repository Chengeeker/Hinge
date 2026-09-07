import 'dart:async';
import 'dart:io';

abstract class DirectoryWatcher {
  String get watchedPath;
  bool get isWatching;
  Stream<String> get changeStream;
  void start();
  void stop();
  void dispose();
}

class DartDirectoryWatcher implements DirectoryWatcher {
  @override
  final String watchedPath;
  final int debounceMs;

  final StreamController<String> _controller =
      StreamController<String>.broadcast();
  StreamSubscription<FileSystemEvent>? _sub;
  Timer? _debounceTimer;
  bool _isWatching = false;

  @override
  bool get isWatching => _isWatching;

  @override
  Stream<String> get changeStream => _controller.stream;

  DartDirectoryWatcher(this.watchedPath, {this.debounceMs = 500});

  @override
  void start() {
    if (_isWatching) return;
    final dir = Directory(watchedPath);
    if (!dir.existsSync()) {
      dir.createSync(recursive: true);
    }

    _isWatching = true;
    _sub = dir.watch(recursive: true).listen((event) {
      _debounceTimer?.cancel();
      _debounceTimer = Timer(Duration(milliseconds: debounceMs), () {
        _controller.add(event.path);
      });
    });
  }

  @override
  void stop() {
    _isWatching = false;
    _sub?.cancel();
    _sub = null;
    _debounceTimer?.cancel();
    _debounceTimer = null;
  }

  @override
  void dispose() {
    stop();
    _controller.close();
  }
}

class MockDirectoryWatcher implements DirectoryWatcher {
  @override
  final String watchedPath;
  final StreamController<String> _controller =
      StreamController<String>.broadcast();
  bool _isWatching = false;

  @override
  bool get isWatching => _isWatching;

  @override
  Stream<String> get changeStream => _controller.stream;

  MockDirectoryWatcher([this.watchedPath = '/mock/sync']);

  @override
  void start() => _isWatching = true;

  @override
  void stop() => _isWatching = false;

  void triggerChange(String path) {
    if (_isWatching) {
      _controller.add(path);
    }
  }

  @override
  void dispose() {
    stop();
    _controller.close();
  }
}
