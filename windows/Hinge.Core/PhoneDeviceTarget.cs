namespace Hinge.Core;

public sealed record PhoneDeviceTarget(string DeviceId, string Name, bool IsConnected)
{
    public string DisplayName => IsConnected ? Name : Name + "（离线）";

    public static IReadOnlyList<PhoneDeviceTarget> ForSendMenu(IEnumerable<PhoneDeviceTarget> targets, string? lastDeviceId) =>
        Merge(targets.Where(device => device.IsConnected ||
            string.Equals(device.DeviceId, lastDeviceId, StringComparison.OrdinalIgnoreCase)));

    public static IReadOnlyList<PhoneDeviceTarget> Merge(IEnumerable<PhoneDeviceTarget> targets, bool connectedOnly = false)
    {
        var devices = targets.Where(d => !string.IsNullOrWhiteSpace(d.DeviceId) && (!connectedOnly || d.IsConnected))
            .GroupBy(d => d.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(d => d.IsConnected).First())
            .Select(d => d with { Name = string.IsNullOrWhiteSpace(d.Name) ? "Android 手机" : d.Name.Trim() })
            .ToArray();
        var names = devices.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return devices.Select(d => names.Contains(d.Name)
                ? d with { Name = $"{d.Name} · {d.DeviceId[..Math.Min(8, d.DeviceId.Length)]}" } : d)
            .OrderByDescending(d => d.IsConnected).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
