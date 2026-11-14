namespace Hinge.Core;

/// <summary>
/// Presentation-only filtering. Discovery and authenticated sessions still see
/// the original complete device/address set; hidden local aliases are never
/// removed from the underlying registry or used to change routing.
/// </summary>
public static class NearbyDevicePresentation
{
    public static IReadOnlyList<Device> Visible(
        IEnumerable<Device> devices, string localDeviceId) =>
        devices.Where(device =>
            !string.Equals(device.DeviceId, localDeviceId, StringComparison.OrdinalIgnoreCase) &&
            !(string.Equals(device.Name?.Trim(), "local", StringComparison.OrdinalIgnoreCase) &&
              device.Platform != DevicePlatform.Android)).ToArray();

    public static string DisplayAddresses(IEnumerable<string> networkAddresses) =>
        string.Join(", ", networkAddresses.Where(address =>
            !string.IsNullOrWhiteSpace(address) &&
            !string.Equals(address.Trim(), "local", StringComparison.OrdinalIgnoreCase)));
}
