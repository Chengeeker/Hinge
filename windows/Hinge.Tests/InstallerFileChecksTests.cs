using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using Hinge.Setup;

namespace Hinge.Tests;

public sealed class InstallerFileChecksTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LockedShellDllIsSkippedOnlyWhenContentMatches(bool identical)
    {
        var path = Path.Combine(Path.GetTempPath(), "hinge-shell-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var installed = CreateShellImage();
            File.WriteAllBytes(path, installed);
            var packaged = (byte[])installed.Clone();
            packaged[^1] ^= 1; // Re-signing changes bytes in the certificate table.
            if (!identical) packaged[^9] ^= 1;
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            {
                using var target = zip.CreateEntry("Hinge.ShellExtension.v1.4.3.hash.dll").Open();
                target.Write(packaged);
            }
            bytes.Position = 0;
            using var archive = new ZipArchive(bytes, ZipArchiveMode.Read);
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Equal(identical, InstallerFileChecks.IsIdenticalShellExtension(archive.Entries[0], path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static byte[] CreateShellImage()
    {
        var image = File.ReadAllBytes(typeof(InstallerFileChecksTests).Assembly.Location);
        using var reader = new PEReader(new MemoryStream(image));
        var header = reader.PEHeaders;
        var directoryStart = header.PEHeaderStartOffset +
            (header.PEHeader!.Magic == PEMagic.PE32Plus ? 112 : 96);
        var certificateEntry = directoryStart + 4 * 8;
        var certificateStart = (image.Length + 7) & ~7;
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(certificateEntry), certificateStart);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(certificateEntry + 4), 8);
        Array.Resize(ref image, certificateStart + 8);
        // The comparison reads the certificate table bounds, not a certificate signature.
        return image;
    }
}
