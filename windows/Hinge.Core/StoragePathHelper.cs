namespace Hinge.Core;

/// <summary>
/// Helper utilities for normalizing and formatting Android storage destinations.
/// </summary>
public static class StoragePathHelper
{
    public const string DefaultDropInbox = "Download/Hinge";

    /// <summary>
    /// Normalizes a folder path into a clean relative path from Android's shared storage root.
    /// Strips leading/trailing slashes, Windows backslashes, and optional "/storage/emulated/0" prefixes.
    /// Returns "Download/Hinge" if the normalized path is empty or matches the storage root.
    /// </summary>
    public static string NormalizeFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return DefaultDropInbox;
        var normalized = path.Replace('\\', '/').Trim('/');
        const string prefix = "storage/emulated/0";
        if (normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultDropInbox;
        }
        if (normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[(prefix.Length + 1)..].Trim('/');
        }
        return string.IsNullOrWhiteSpace(normalized) ? DefaultDropInbox : normalized;
    }

    /// <summary>
    /// Gets the display name of a folder (the final segment of the relative path).
    /// </summary>
    public static string GetDisplayFolderName(string path)
    {
        var normalized = NormalizeFolderPath(path);
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 && lastSlash < normalized.Length - 1
            ? normalized[(lastSlash + 1)..]
            : normalized;
    }
}
