namespace Hinge.Core;

/// <summary>
/// The trusted-device database is durable pairing history, not a list of
/// available workspaces. Only authenticated, ready Android sessions belong in
/// the active-device picker. Keep the explicitly selected offline identity
/// visible as a single choice, without silently redirecting operations.
/// </summary>
public static class WorkspaceDeviceChoices
{
    public static IReadOnlyList<PhoneDeviceTarget> ForSelector(
        IEnumerable<PhoneDeviceTarget> knownDevices, string? selectedDeviceId)
    {
        var known = knownDevices.ToArray();
        var connected = PhoneDeviceTarget.Merge(known, connectedOnly: true);
        if (string.IsNullOrWhiteSpace(selectedDeviceId) ||
            connected.Any(device => string.Equals(device.DeviceId, selectedDeviceId,
                StringComparison.OrdinalIgnoreCase)))
            return connected;

        var selected = known.FirstOrDefault(device =>
            string.Equals(device.DeviceId, selectedDeviceId,
                StringComparison.OrdinalIgnoreCase));

        // An offline device is never substituted with some other phone.
        return connected.Append(new PhoneDeviceTarget(selectedDeviceId,
            selected?.Name ?? "上次选择的手机", false)).ToArray();
    }
}
