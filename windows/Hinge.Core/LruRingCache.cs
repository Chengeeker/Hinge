namespace Hinge.Core;

/// <summary>
/// Thread-safe LRU ring cache for anti-looping deduplication (default capacity: 100).
/// </summary>
public class LruRingCache
{
    private readonly int _capacity;
    private readonly object _lock = new();
    private readonly LinkedList<string> _order = new();
    private readonly HashSet<string> _lookup = new();

    public int Capacity => _capacity;
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _lookup.Count;
            }
        }
    }

    public LruRingCache(int capacity = 100)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }
        _capacity = capacity;
    }

    /// <summary>
    /// Checks whether the cache contains the specified identifier.
    /// </summary>
    public bool Contains(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        lock (_lock)
        {
            return _lookup.Contains(id);
        }
    }

    /// <summary>
    /// Adds an identifier to the cache. If already present, refreshes its LRU position.
    /// If capacity is exceeded, evicts the oldest item.
    /// Returns true if newly added; false if already present.
    /// </summary>
    public bool Add(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;

        lock (_lock)
        {
            if (_lookup.Contains(id))
            {
                _order.Remove(id);
                _order.AddLast(id);
                return false;
            }

            if (_lookup.Count >= _capacity)
            {
                var oldest = _order.First;
                if (oldest != null)
                {
                    _lookup.Remove(oldest.Value);
                    _order.RemoveFirst();
                }
            }

            _lookup.Add(id);
            _order.AddLast(id);
            return true;
        }
    }

    /// <summary>
    /// Clears the cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _lookup.Clear();
            _order.Clear();
        }
    }
}
