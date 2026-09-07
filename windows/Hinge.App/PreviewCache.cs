using System.Security.Cryptography;
using System.Text;

namespace Hinge.App;

/// <summary>
/// Temporary files used when a remote item is opened by a Windows default app.
/// These files are deliberately kept outside Downloads and are pruned by age
/// and size so opening media does not silently create a permanent copy.
/// </summary>
public sealed class PreviewCache
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private const long MaxCacheBytes = 512L * 1024 * 1024;

    public PreviewCache()
    {
        RootDirectory = Path.Combine(Path.GetTempPath(), "Hinge", "PreviewCache");
        Directory.CreateDirectory(RootDirectory);
        Prune();
    }

    public string RootDirectory { get; }

    public PreviewLocation GetLocation(RemoteFileEntry entry)
    {
        var identity = $"{entry.Uri}\n{entry.SizeBytes}\n{entry.ModifiedAt}";
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        var directory = Path.Combine(RootDirectory, hash);
        var fileName = SanitizeFileName(entry.Name);
        return new PreviewLocation(directory, Path.Combine(directory, fileName));
    }

    public bool IsUsable(PreviewLocation location, long expectedSize)
    {
        if (!File.Exists(location.FilePath)) return false;
        if (expectedSize <= 0) return new FileInfo(location.FilePath).Length > 0;
        return new FileInfo(location.FilePath).Length == expectedSize;
    }

    public void Prepare(PreviewLocation location) => Directory.CreateDirectory(location.Directory);

    public void Remove(PreviewLocation location)
    {
        try
        {
            if (Directory.Exists(location.Directory))
            {
                Directory.Delete(location.Directory, recursive: true);
            }
        }
        catch
        {
            // A default app may still have a handle open. The next startup
            // will retry cleanup after that handle is released.
        }
    }

    public void Prune(string? preserveDirectory = null)
    {
        try
        {
            if (!Directory.Exists(RootDirectory)) return;

            var now = DateTime.UtcNow;
            var entries = Directory.GetDirectories(RootDirectory)
                .Select(path => new DirectoryInfo(path))
                .OrderBy(info => info.LastWriteTimeUtc)
                .ToList();

            foreach (var entry in entries.ToArray())
            {
                if (preserveDirectory != null &&
                    string.Equals(entry.FullName, preserveDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (now - entry.LastWriteTimeUtc > MaxAge)
                {
                    TryDelete(entry.FullName);
                    entries.Remove(entry);
                }
            }

            long totalBytes = entries.Sum(GetDirectorySize);
            foreach (var entry in entries)
            {
                if (totalBytes <= MaxCacheBytes) break;
                if (preserveDirectory != null &&
                    string.Equals(entry.FullName, preserveDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var size = GetDirectorySize(entry);
                TryDelete(entry.FullName);
                totalBytes -= size;
            }
        }
        catch
        {
            // Cache maintenance must never prevent a preview from opening.
        }
    }

    private static string SanitizeFileName(string? name)
    {
        var safeName = Path.GetFileName(name ?? string.Empty);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(safeName) ? "preview" : safeName;
    }

    private static long GetDirectorySize(DirectoryInfo directory)
    {
        try
        {
            return directory.Exists
                ? directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length)
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Ignore files locked by an external preview application.
        }
    }
}

public sealed record PreviewLocation(string Directory, string FilePath);
