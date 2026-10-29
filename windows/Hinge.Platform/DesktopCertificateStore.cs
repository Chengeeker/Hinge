using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace Hinge.Platform;

public static class DesktopCertificateStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<X509Certificate2> LoadOrCreateAsync()
    {
        await Gate.WaitAsync();
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hinge", "desktop-server-identity.dat");
            if (File.Exists(path))
            {
                var encrypted = CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(path));
                var plain = await new DataProtectionProvider().UnprotectAsync(encrypted);
                CryptographicBuffer.CopyToByteArray(plain, out byte[] pfx);
                try
                {
                    // Schannel on supported Windows versions cannot use EphemeralKeySet for server TLS.
                    var certificate = new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.UserKeySet);
                    if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
                    {
                        certificate.Dispose();
                        throw new CryptographicException("电脑桌面身份已失效，请在远控设置中重新配对。");
                    }
                    return certificate;
                }
                finally { CryptographicOperations.ZeroMemory(pfx); }
            }

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=Hinge Desktop", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
            byte[] exported = created.Export(X509ContentType.Pfx);
            try
            {
                var protectedBuffer = await new DataProtectionProvider("LOCAL=user").ProtectAsync(
                    CryptographicBuffer.CreateFromByteArray(exported));
                CryptographicBuffer.CopyToByteArray(protectedBuffer, out byte[] encrypted);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path + ".tmp", encrypted);
                File.Move(path + ".tmp", path, true);
                return new X509Certificate2(exported, (string?)null, X509KeyStorageFlags.UserKeySet);
            }
            finally { CryptographicOperations.ZeroMemory(exported); }
        }
        finally { Gate.Release(); }
    }
}
