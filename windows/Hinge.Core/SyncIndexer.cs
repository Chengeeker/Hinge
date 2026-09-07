using System.Security.Cryptography;

namespace Hinge.Core;

public static class SyncIndexer
{
    public static async Task<SyncManifestMessage> GenerateManifestAsync(
        string rootPath,
        string folderId = "default",
        bool computeHashOnDemand = false)
    {
        var manifest = new SyncManifestMessage
        {
            FolderId = folderId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        if (!Directory.Exists(rootPath))
        {
            return manifest;
        }

        var dirInfo = new DirectoryInfo(rootPath);
        var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            // Normalize path to relative with forward slashes
            string relative = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
            long size = file.Length;
            long modTime = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds();

            string sha256 = computeHashOnDemand ? await ComputeSha256Async(file.FullName) : string.Empty;

            manifest.Entries.Add(new SyncFileEntry
            {
                RelativePath = relative,
                Size = size,
                ModifiedTime = modTime,
                Sha256 = sha256,
                PlatformFileIdentity = null
            });
        }

        return manifest;
    }

    public static SyncDifference CalculateDifference(SyncManifestMessage localManifest, SyncManifestMessage remoteManifest)
    {
        var diff = new SyncDifference();

        var localMap = localManifest.Entries.ToDictionary(e => e.RelativePath, e => e, StringComparer.OrdinalIgnoreCase);
        var remoteMap = remoteManifest.Entries.ToDictionary(e => e.RelativePath, e => e, StringComparer.OrdinalIgnoreCase);

        // Check remote entries against local
        foreach (var (remotePath, remoteEntry) in remoteMap)
        {
            if (!localMap.TryGetValue(remotePath, out var localEntry))
            {
                // Remote has it, local does not -> need to pull from remote
                diff.NeedPull.Add(remoteEntry);
            }
            else
            {
                // Tier 1: Fast metadata match (Size and MTime within 1s tolerance for cross-FS clock precision)
                bool metadataMatch = localEntry.Size == remoteEntry.Size &&
                                     Math.Abs(localEntry.ModifiedTime - remoteEntry.ModifiedTime) < 1000;

                // Tier 2: Hash match if hashes were pre-calculated
                bool hashMatch = !string.IsNullOrEmpty(localEntry.Sha256) &&
                                 !string.IsNullOrEmpty(remoteEntry.Sha256) &&
                                 string.Equals(localEntry.Sha256, remoteEntry.Sha256, StringComparison.OrdinalIgnoreCase) &&
                                 localEntry.Size == remoteEntry.Size;

                if (metadataMatch || hashMatch)
                {
                    diff.UpToDate.Add(localEntry);
                }
                else
                {
                    // Conflict / modification: Last-Write-Wins
                    if (remoteEntry.ModifiedTime > localEntry.ModifiedTime)
                    {
                        diff.NeedPull.Add(remoteEntry);
                    }
                    else if (localEntry.ModifiedTime > remoteEntry.ModifiedTime)
                    {
                        diff.NeedPush.Add(localEntry);
                    }
                    else
                    {
                        // Deterministic tie-break by SHA-256 or size
                        if (string.CompareOrdinal(remoteEntry.Sha256, localEntry.Sha256) > 0)
                        {
                            diff.NeedPull.Add(remoteEntry);
                        }
                        else
                        {
                            diff.NeedPush.Add(localEntry);
                        }
                    }
                }
            }
        }

        // Check local entries not in remote -> need to push
        foreach (var (localPath, localEntry) in localMap)
        {
            if (!remoteMap.ContainsKey(localPath))
            {
                diff.NeedPush.Add(localEntry);
            }
        }

        return diff;
    }

    private static async Task<string> ComputeSha256Async(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        using var sha = SHA256.Create();
        byte[] hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
