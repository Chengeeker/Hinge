namespace Hinge.Core;

/// <summary>
/// Explicit device workspace identity is distinct from a TCP session.
/// Network callbacks must never silently switch the user's selected phone.
/// </summary>
public sealed class DeviceWorkspaceManager
{
    private readonly object _gate = new();
    private string? _selectedDeviceId;
    private bool _explicitlySelected;
    private readonly Dictionary<string, string> _pageOverrides =
        new(StringComparer.OrdinalIgnoreCase);

    public string? SelectedDeviceId
    {
        get { lock (_gate) return _selectedDeviceId; }
    }

    public bool HasExplicitSelection
    {
        get { lock (_gate) return _explicitlySelected; }
    }

    public void RestoreSelection(string? deviceId)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return;
            _selectedDeviceId = deviceId;
            _explicitlySelected = true;
        }
    }

    public void SelectDevice(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        lock (_gate)
        {
            _selectedDeviceId = deviceId;
            _explicitlySelected = true;
        }
    }

    public void AdoptFirstConnected(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        lock (_gate)
        {
            if (_selectedDeviceId == null)
                _selectedDeviceId = deviceId;
        }
    }

    public void SetPageOverride(string pageKey, string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        lock (_gate) _pageOverrides[pageKey] = deviceId;
    }

    public void ClearPageOverride(string pageKey)
    {
        lock (_gate) _pageOverrides.Remove(pageKey);
    }

    public string? TargetForPage(string pageKey)
    {
        lock (_gate)
            return _pageOverrides.TryGetValue(pageKey, out var id) ? id : _selectedDeviceId;
    }

    public bool IsSelected(string? deviceId)
    {
        lock (_gate)
            return !string.IsNullOrWhiteSpace(deviceId) &&
                string.Equals(_selectedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase);
    }

    public static string CacheKey(string deviceId, string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        return deviceId.Trim().ToLowerInvariant() + ":" + resourceId;
    }
}
