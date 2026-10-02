using System.IO.Compression;
using System.Security.Cryptography;

namespace Hinge.Setup;

internal static class InstallerFileChecks
{
    public static bool IsIdenticalShellExtension(ZipArchiveEntry entry, string path)
    {
        if (!entry.Name.StartsWith("Hinge.ShellExtension.v", StringComparison.OrdinalIgnoreCase) ||
            !entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return false;
        try
        {
            if (new FileInfo(path).Length != entry.Length) return false;
            using var installed = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var packaged = entry.Open();
            return SHA256.HashData(installed).AsSpan().SequenceEqual(SHA256.HashData(packaged));
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
