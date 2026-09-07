import 'dart:collection';

/// LRU Ring Cache for anti-looping deduplication (default capacity: 100).
class LruRingCache {
  final int capacity;
  final LinkedHashSet<String> _set = LinkedHashSet<String>();

  LruRingCache([this.capacity = 100]) {
    if (capacity <= 0) {
      throw ArgumentError.value(
        capacity,
        'capacity',
        'Capacity must be positive.',
      );
    }
  }

  int get count => _set.length;

  bool contains(String id) {
    if (id.isEmpty) return false;
    return _set.contains(id);
  }

  /// Adds an identifier to the cache.
  /// If already present, moves to the most recent end and returns false.
  /// If newly added and capacity is exceeded, evicts the oldest item.
  /// Returns true if newly added, false if already present.
  bool add(String id) {
    if (id.isEmpty) return false;

    if (_set.contains(id)) {
      _set.remove(id);
      _set.add(id);
      return false;
    }

    if (_set.length >= capacity) {
      final oldest = _set.first;
      _set.remove(oldest);
    }

    _set.add(id);
    return true;
  }

  void clear() {
    _set.clear();
  }
}
