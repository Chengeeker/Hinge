namespace Hinge.Core;

/// <summary>
/// Remote notification actions MUST target the phone that generated them.
/// An unscoped legacy action may use the only online phone, but is never
/// broadcast when two distinct devices have live sessions.
/// </summary>
public static class NotificationRoutingPolicy
{
    public static IReadOnlyList<string> TargetIds(
        IEnumerable<string?> connectedDeviceIds, string? sourceDeviceId)
    {
        var ids = connectedDeviceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(sourceDeviceId))
            return ids.Where(id => string.Equals(id, sourceDeviceId,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        return ids.Length == 1 ? ids : Array.Empty<string>();
    }
}
