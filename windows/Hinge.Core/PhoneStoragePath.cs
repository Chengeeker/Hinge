namespace Hinge.Core;

/// <summary>
/// The Android MediaStore URI is an identifier, not a filesystem location.
/// Prefer the phone's actual DATA path, then a complete MediaStore
/// RELATIVE_PATH, and never invent a directory from an orphaned filename.
/// </summary>
public static class PhoneStoragePath
{
    public const string PrimaryStorage = "/storage/emulated/0";

    public static string? Resolve(string? relativePath, string? absolutePath = null,
        bool explicitStorageListing = false)
    {
        if (!string.IsNullOrWhiteSpace(absolutePath))
        {
            var direct = Normalize(absolutePath);
            if (direct.StartsWith("/storage/", StringComparison.Ordinal) &&
                !HasTraversal(direct))
                return direct;
        }

        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var relative = Normalize(relativePath).Trim();
        if (HasTraversal(relative)) return null;
        if (relative.StartsWith("/storage/", StringComparison.Ordinal)) return relative;

        // MediaStore can return only the filename when RELATIVE_PATH is
        // unavailable. No real directory is known in that case.
        relative = relative.TrimStart('/');
        if (relative.Length == 0 || (!explicitStorageListing && !relative.Contains('/'))) return null;
        return PrimaryStorage + "/" + relative;
    }

    public static string? Directory(string? relativePath, string? absolutePath = null,
        bool explicitStorageListing = false)
    {
        var full = Resolve(relativePath, absolutePath, explicitStorageListing);
        if (full == null) return null;
        var lastSlash = full.LastIndexOf('/');
        return lastSlash > 0 ? full[..lastSlash] : null;
    }

    private static bool HasTraversal(string path) =>
        path.Split('/').Any(segment => segment == "." || segment == "..");

    private static string Normalize(string path) => path.Replace('\\', '/');
}
