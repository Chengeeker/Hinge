using System.Security.Cryptography;
using System.Text;

namespace Hinge.Core;

/// <summary>
/// The small out-of-band BLE signal used to wake an associated Android Hinge
/// companion. It is deliberately not a transport protocol: LAN pairing and
/// the existing authenticated TCP session remain the trust boundary.
/// </summary>
public enum WakeAdvertisementKind : byte
{
    Pairing = 1,
    FileTransfer = 2,
}

public static class HingeWakeProtocol
{
    // This is a private application identifier for the local companion
    // advertisement, not a claim of ownership of a Bluetooth SIG company ID.
    // The payload is useful only to an explicitly associated Hinge companion.
    public const ushort CompanyId = 0xFFFE;
    public const byte ProtocolVersion = 1;
    public const int DeviceTagLength = 8;

    private static readonly byte[] Magic = "HGW"u8.ToArray();

    public static byte[] CreatePayload(string deviceId, WakeAdvertisementKind kind)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("设备 ID 不能为空。", nameof(deviceId));
        }

        var payload = new byte[Magic.Length + 2 + DeviceTagLength];
        Magic.CopyTo(payload, 0);
        payload[Magic.Length] = ProtocolVersion;
        payload[Magic.Length + 1] = (byte)kind;

        var tag = SHA256.HashData(Encoding.UTF8.GetBytes(deviceId.Trim()));
        Array.Copy(tag, 0, payload, Magic.Length + 2, DeviceTagLength);
        return payload;
    }

    public static bool IsWakePayload(ReadOnlySpan<byte> payload)
    {
        return payload.Length >= Magic.Length + 2 &&
            payload[..Magic.Length].SequenceEqual(Magic) &&
            payload[Magic.Length] == ProtocolVersion &&
            payload[Magic.Length + 1] is (byte)WakeAdvertisementKind.Pairing or
                (byte)WakeAdvertisementKind.FileTransfer;
    }

    /// <summary>
    /// Returns the prefix used by Android CompanionDeviceManager's
    /// manufacturer-data filter. The device tag and wake kind are intentionally
    /// left unconstrained so one association can wake for both workflows.
    /// </summary>
    public static byte[] FilterData() =>
        [.. Magic, ProtocolVersion];

    public static byte[] FilterMask() =>
        [.. Enumerable.Repeat((byte)0xFF, Magic.Length + 1)];
}
