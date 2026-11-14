using System.Text.RegularExpressions;

namespace Hinge.App;

internal static partial class WirelessAdbClient
{
    private const string HingeIdentityUri = DiagnosticsProvider + "/identity";

    /// <summary>
    /// ADB transports are not Hinge TCP sessions. Never assume the first
    /// connected adb device (or the last paired one) is the selected phone.
    /// Match the pseudonymous Hinge identity returned by that exact Android
    /// installation before starting scrcpy or sending shell commands.
    /// </summary>
    internal static async Task<string?> FindTransportForHingeDeviceAsync(
        string deviceId,
        CancellationToken cancellationToken,
        string? pairedHost = null,
        string? pairedPort = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Select a Hinge phone", nameof(deviceId));

        var existing = await ProbeConnectedTransportsAsync(deviceId, cancellationToken);
        if (existing != null) return existing;

        // Only attempt a known device-specific endpoint saved after pairing,
        // never blindly connect to the most recently paired ADB device.
        if (string.IsNullOrWhiteSpace(pairedHost) ||
            string.IsNullOrWhiteSpace(pairedPort) ||
            TryEndpoint(pairedHost, pairedPort) is not { } endpoint)
            return null;

        var result = await RunAsync(cancellationToken, "connect", endpoint);
        if (result.ExitCode != 0 || IsAdbConnectionFailure(result.Output, result.Error))
            return null;

        return await ProbeConnectedTransportsAsync(deviceId, cancellationToken);
    }

    private static async Task<string?> ProbeConnectedTransportsAsync(
        string deviceId, CancellationToken cancellationToken)
    {
        var devices = await RunAsync(cancellationToken, "devices", "-l");
        EnsureSuccess(devices);
        foreach (var (transport, state) in ParseDeviceStates(devices.Output))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state != "device" || !IsWirelessTransport(transport)) continue;
            try
            {
                var identity = await RunAsync(cancellationToken, "-s", transport, "shell",
                    "content", "read", "--uri", HingeIdentityUri);
                if (identity.ExitCode != 0) continue;
                var match = Regex.Match(identity.Output,
                    @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b");
                if (match.Success && string.Equals(match.Value, deviceId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    await ValidateScreenTransportAsync(transport, cancellationToken);
                    return transport;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Some phones have no Hinge or an older version installed.
                // An unverified transport must not be selected.
            }
        }
        return null;
    }
}
