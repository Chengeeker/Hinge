namespace Hinge.Core;

/// <summary>
/// Serializes attempts for a single device without blocking unrelated phones.
/// A manual disconnect invalidates only that device's in-flight attempt.
/// </summary>
public sealed class DeviceConnectionAttemptTracker
{
    private readonly object _sync = new();
    private readonly Dictionary<string, long> _versions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _connecting = new(StringComparer.OrdinalIgnoreCase);

    public bool IsConnecting(string deviceId)
    {
        lock (_sync) return _connecting.Contains(deviceId);
    }

    public bool TryBegin(string deviceId, out long generation)
    {
        lock (_sync)
        {
            generation = 0;
            if (string.IsNullOrWhiteSpace(deviceId) || !_connecting.Add(deviceId)) return false;
            _versions.TryGetValue(deviceId, out var previous);
            generation = checked(previous + 1);
            _versions[deviceId] = generation;
            return true;
        }
    }

    public bool IsCurrent(string deviceId, long generation)
    {
        lock (_sync)
            return _connecting.Contains(deviceId) &&
                _versions.TryGetValue(deviceId, out var current) && current == generation;
    }

    public void Cancel(string deviceId)
    {
        lock (_sync)
        {
            _versions.TryGetValue(deviceId, out var previous);
            _versions[deviceId] = checked(previous + 1);
        }
    }

    public void CancelAll()
    {
        lock (_sync)
        {
            foreach (var deviceId in _connecting)
            {
                _versions.TryGetValue(deviceId, out var previous);
                _versions[deviceId] = checked(previous + 1);
            }
        }
    }

    public void End(string deviceId, long generation)
    {
        lock (_sync)
        {
            // TryBegin cannot start another attempt for this ID until End,
            // even if Cancel was called repeatedly in the meantime.
            _connecting.Remove(deviceId);
        }
    }
}
