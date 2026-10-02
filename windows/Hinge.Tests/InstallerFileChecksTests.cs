using System.IO.Compression;
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
            File.WriteAllBytes(path, [1, 2, 3]);
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            {
                using var target = zip.CreateEntry("Hinge.ShellExtension.v1.4.3.hash.dll").Open();
                target.Write(identical ? new byte[] { 1, 2, 3 } : new byte[] { 3, 2, 1 });
            }
            bytes.Position = 0;
            using var archive = new ZipArchive(bytes, ZipArchiveMode.Read);
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Equal(identical, InstallerFileChecks.IsIdenticalShellExtension(archive.Entries[0], path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
