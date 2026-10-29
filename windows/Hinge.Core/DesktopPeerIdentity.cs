using System.Security.Cryptography;
using System.Text;

namespace Hinge.Core;

public static class DesktopPeerIdentity
{
    public static string Fingerprint(ReadOnlySpan<byte> certificate)
    {
        if (certificate.Length is < 32 or > 16384) throw new ArgumentException("Invalid desktop certificate size.");
        return Convert.ToHexString(SHA256.HashData(certificate));
    }

    // Both screens display the same 64-bit code before persisting a new mutual-TLS peer.
    public static string VerificationCode(ReadOnlySpan<byte> serverCertificate, ReadOnlySpan<byte> clientCertificate)
    {
        byte[] transcript = Encoding.ASCII.GetBytes("Hinge Desktop Pairing v1\0" +
            Fingerprint(serverCertificate) + Fingerprint(clientCertificate));
        string code = Convert.ToHexString(SHA256.HashData(transcript)).Substring(0, 16);
        return string.Join(" ", Enumerable.Range(0, 4).Select(i => code.Substring(i * 4, 4)));
    }
}
