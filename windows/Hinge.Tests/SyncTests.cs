using System.Text.Json;
using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public class SyncTests
{
    [Fact]
    public async Task SyncIndexer_GenerateManifest_ScansSubdirectoriesAndComputesHash()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"sync_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create root file
            string rootFile = Path.Combine(tempDir, "root.txt");
            await File.WriteAllTextAsync(rootFile, "Hello Sync Root");

            // Create subdirectory and file
            string subDir = Path.Combine(tempDir, "subfolder");
            Directory.CreateDirectory(subDir);
            string subFile = Path.Combine(subDir, "child.dat");
            await File.WriteAllBytesAsync(subFile, new byte[] { 1, 2, 3, 4, 5 });

            // Test on-demand hash computation
            var manifestWithHash = await SyncIndexer.GenerateManifestAsync(tempDir, "test-folder", computeHashOnDemand: true);

            Assert.Equal("test-folder", manifestWithHash.FolderId);
            Assert.Equal(2, manifestWithHash.Entries.Count);

            var rootEntry = manifestWithHash.Entries.FirstOrDefault(e => e.RelativePath == "root.txt");
            Assert.NotNull(rootEntry);
            Assert.True(rootEntry.Size > 0);
            Assert.Equal(64, rootEntry.Sha256.Length); // 64 hex chars for SHA-256

            var subEntry = manifestWithHash.Entries.FirstOrDefault(e => e.RelativePath == "subfolder/child.dat");
            Assert.NotNull(subEntry);
            Assert.Equal(5, subEntry.Size);
            Assert.Equal(64, subEntry.Sha256.Length);

            // Test fast metadata scan (zero hash I/O)
            var fastManifest = await SyncIndexer.GenerateManifestAsync(tempDir, "test-folder", computeHashOnDemand: false);
            Assert.Equal(2, fastManifest.Entries.Count);
            Assert.Empty(fastManifest.Entries[0].Sha256);
            Assert.Empty(fastManifest.Entries[1].Sha256);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void SyncIndexer_CalculateDifference_CorrectlyClassifiesEntries()
    {
        var localManifest = new SyncManifestMessage
        {
            FolderId = "folder-1",
            Entries = new List<SyncFileEntry>
            {
                // Identical file
                new() { RelativePath = "shared/same.txt", Size = 100, ModifiedTime = 1000, Sha256 = "abc123hash" },
                // Local is newer
                new() { RelativePath = "shared/local_newer.txt", Size = 200, ModifiedTime = 2000, Sha256 = "localhash2" },
                // Only in local
                new() { RelativePath = "local_only.txt", Size = 50, ModifiedTime = 1000, Sha256 = "localonlyhash" }
            }
        };

        var remoteManifest = new SyncManifestMessage
        {
            FolderId = "folder-1",
            Entries = new List<SyncFileEntry>
            {
                // Identical file
                new() { RelativePath = "shared/same.txt", Size = 100, ModifiedTime = 1000, Sha256 = "abc123hash" },
                // Remote is older
                new() { RelativePath = "shared/local_newer.txt", Size = 180, ModifiedTime = 1500, Sha256 = "remoteolderhash" },
                // Remote is newer
                new() { RelativePath = "shared/remote_newer.txt", Size = 300, ModifiedTime = 3000, Sha256 = "remotenewhash" },
                // Only in remote
                new() { RelativePath = "remote_only.txt", Size = 80, ModifiedTime = 1000, Sha256 = "remoteonlyhash" }
            }
        };

        // Add remote_newer to local with older timestamp
        localManifest.Entries.Add(new SyncFileEntry
        {
            RelativePath = "shared/remote_newer.txt",
            Size = 250,
            ModifiedTime = 2500,
            Sha256 = "localolderhash"
        });

        var diff = SyncIndexer.CalculateDifference(localManifest, remoteManifest);

        // UpToDate: shared/same.txt
        Assert.Single(diff.UpToDate);
        Assert.Equal("shared/same.txt", diff.UpToDate[0].RelativePath);

        // NeedPull: remote_only.txt and shared/remote_newer.txt
        Assert.Equal(2, diff.NeedPull.Count);
        Assert.Contains(diff.NeedPull, e => e.RelativePath == "remote_only.txt");
        Assert.Contains(diff.NeedPull, e => e.RelativePath == "shared/remote_newer.txt");

        // NeedPush: local_only.txt and shared/local_newer.txt
        Assert.Equal(2, diff.NeedPush.Count);
        Assert.Contains(diff.NeedPush, e => e.RelativePath == "local_only.txt");
        Assert.Contains(diff.NeedPush, e => e.RelativePath == "shared/local_newer.txt");
    }

    [Fact]
    public void SyncModel_SerializationRoundtrip()
    {
        var manifest = new SyncManifestMessage
        {
            FolderId = "docs-vault",
            Timestamp = 1788500000000,
            Entries = new List<SyncFileEntry>
            {
                new()
                {
                    RelativePath = "plans/project.pdf",
                    Size = 1048576,
                    ModifiedTime = 1788499000000,
                    Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
                }
            }
        };

        string json = JsonSerializer.Serialize(manifest);
        var parsed = JsonSerializer.Deserialize<SyncManifestMessage>(json);

        Assert.NotNull(parsed);
        Assert.Equal(manifest.FolderId, parsed.FolderId);
        Assert.Equal(manifest.Timestamp, parsed.Timestamp);
        Assert.Single(parsed.Entries);
        Assert.Equal("plans/project.pdf", parsed.Entries[0].RelativePath);
        Assert.Equal(1048576, parsed.Entries[0].Size);
        Assert.Equal(manifest.Entries[0].Sha256, parsed.Entries[0].Sha256);
    }
}
