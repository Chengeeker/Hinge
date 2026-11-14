namespace Hinge.Core;

/// <summary>
/// Presentation-only filtering. Underlying discovery, authenticated sessions,
/// trusted identities and connection routing are deliberately not mutated.
/// </summary>
public static class NearbyDevicePresentation
{
    public static IReadOnlyList<Device> Visible(
        IEnumerable<Device> devices, string localDeviceId)
    {
        var candidates = devices.Where(device =>
            !string.Equals(device.DeviceId, localDeviceId, StringComparison.OrdinalIgnoreCase) &&
            !(string.Equals(device.Name?.Trim(), "local", StringComparison.OrdinalIgnoreCase) &&
              device.Platform != DevicePlatform.Android)).ToArray();

        // A phone reinstall/identity migration may leave an old DeviceId in
        // the runtime discovery registry. When that same model is presently
        // discoverable under another ID, hide only the disconnected row.
        // Never merge IDs or collapse two independently live Android devices.
        // If the old identity starts announcing again it becomes visible.
        var presentPhones = candidates.Where(device =>
            device.Platform == DevicePlatform.Android &&
            device.ConnectionState != ConnectionState.Disconnected).ToArray();

        return candidates.Where(device =>
            device.Platform != DevicePlatform.Android ||
            device.ConnectionState != ConnectionState.Disconnected ||
            !presentPhones.Any(live =>
                !string.Equals(live.DeviceId, device.DeviceId,
                    StringComparison.OrdinalIgnoreCase) &&
                IsSameModel(live, device))).ToArray();
    }

    private static bool IsSameModel(Device left, Device right)
    {
        if (string.IsNullOrWhiteSpace(left.Name) ||
            !string.Equals(left.Name.Trim(), right.Name?.Trim(),
                StringComparison.OrdinalIgnoreCase))
            return false;

        // Incomplete UDP announcements are common; a known disagreement in
        // model or manufacturer is evidence of different physical devices.
        if (!string.IsNullOrWhiteSpace(left.Manufacturer) &&
            !string.IsNullOrWhiteSpace(right.Manufacturer) &&
            !string.Equals(left.Manufacturer.Trim(), right.Manufacturer.Trim(),
                StringComparison.OrdinalIgnoreCase))
            return false;

        return string.IsNullOrWhiteSpace(left.Model) ||
            string.IsNullOrWhiteSpace(right.Model) ||
            string.Equals(left.Model.Trim(), right.Model.Trim(),
                StringComparison.OrdinalIgnoreCase);
    }

    public static string DisplayAddresses(IEnumerable<string> networkAddresses) =>
        string.Join(", ", networkAddresses.Where(address =>
            !string.IsNullOrWhiteSpace(address) &&
            !string.Equals(address.Trim(), "local", StringComparison.OrdinalIgnoreCase)));
}
