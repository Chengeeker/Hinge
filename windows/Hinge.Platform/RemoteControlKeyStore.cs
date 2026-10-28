using System.Security.Cryptography;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace Hinge.Platform;

public static class RemoteControlKeyStore
{
    public static async Task<byte[]> LoadOrCreateAsync(bool rotate = false)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hinge", "remote-control-key.dat");
        if (!rotate && File.Exists(path))
        {
            var protectedBytes = CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(path));
            var plain = await new DataProtectionProvider().UnprotectAsync(protectedBytes);
            CryptographicBuffer.CopyToByteArray(plain, out var key);
            if (key.Length != 32) throw new CryptographicException("Invalid stored control key.");
            return key;
        }
        var created = RandomNumberGenerator.GetBytes(32);
        var encrypted = await new DataProtectionProvider("LOCAL=user").ProtectAsync(CryptographicBuffer.CreateFromByteArray(created));
        CryptographicBuffer.CopyToByteArray(encrypted, out var bytes);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path + ".tmp", bytes);
        File.Move(path + ".tmp", path, true);
        return created;
    }
}
