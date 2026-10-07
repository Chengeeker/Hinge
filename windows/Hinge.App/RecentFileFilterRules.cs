namespace Hinge.App;

internal static class RecentFileFilterRules
{
    public const int HideThreshold = 80;

    private static readonly HashSet<string> StrongCacheDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "cache", ".cache", "code_cache", "app_webview", ".thumbnails", "thumbnails"
    };

    private static readonly HashSet<string> UserContentDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "download", "downloads", "documents", "dcim", "pictures", "movies", "music"
    };

    public static bool ShouldHide(
        string? name,
        string? relativePath,
        string? mimeType,
        long sizeBytes)
    {
        var fileName = GetFileName(name);
        if (IsExtensionless(fileName) || sizeBytes >= 0 && sizeBytes < 10 * 1024)
        {
            return true;
        }

        return GetSuspicionScore(name, relativePath, mimeType, sizeBytes) >= HideThreshold;
    }

    public static int GetSuspicionScore(
        string? name,
        string? relativePath,
        string? mimeType,
        long sizeBytes)
    {
        var fileName = GetFileName(name);
        var lowerName = fileName.ToLowerInvariant();
        var stem = GetStem(lowerName);
        var directories = GetDirectorySegments(relativePath, lowerName);
        var temporaryName = HasTemporaryNameMarker(fileName);
        var score = 0;

        var strongCachePath = directories.Any(StrongCacheDirectories.Contains);
        if (strongCachePath) score += 100;
        if (directories.Any(segment => segment is "tmp" or "temp")) score += 80;
        if (directories.Any(segment => segment is "logs" or "log")) score += 40;
        if (directories.Any(segment => segment is "databases" or "shared_prefs" or "no_backup")) score += 40;
        if (ContainsAdjacentSegments(directories, "android", "data") ||
            ContainsAdjacentSegments(directories, "android", "obb"))
        {
            score += 30;
        }

        var definitiveName = lowerName == ".nomedia" || lowerName == "cache" ||
            lowerName.StartsWith(".thumbdata", StringComparison.Ordinal);
        if (definitiveName) score += 100;

        if (lowerName.StartsWith("cache_", StringComparison.Ordinal) ||
            lowerName.StartsWith("thumb_", StringComparison.Ordinal) ||
            lowerName.StartsWith("thumbnail_", StringComparison.Ordinal) ||
            lowerName.StartsWith("temp_", StringComparison.Ordinal) ||
            lowerName.StartsWith("tmp_", StringComparison.Ordinal))
        {
            score += 70;
        }
        if (lowerName.Contains("%3a%2f%2f", StringComparison.Ordinal) ||
            lowerName.Contains("%3a%252f%252f", StringComparison.Ordinal) ||
            lowerName.Contains("%2f%2f", StringComparison.Ordinal))
        {
            score += 70;
        }
        if (HasAnySuffix(lowerName, ".tmp", ".temp", ".cache", ".part", ".crdownload", ".download"))
        {
            score += 70;
        }
        if (HasAnySuffix(lowerName, ".log", ".trace")) score += 40;
        if (HasAnySuffix(lowerName, ".db-shm", ".db-wal", ".lock", ".lck")) score += 70;
        if (lowerName.StartsWith('.')) score += 30;

        if (Guid.TryParse(stem, out _) || Guid.TryParse(lowerName, out _)) score += 25;
        if (LooksLikeHexHash(stem)) score += 20;
        if (IsExtensionless(lowerName)) score += 10;
        if (sizeBytes >= 0 && sizeBytes < 4 * 1024) score += 10;

        if (HasKnownMimeType(mimeType)) score -= 20;
        if (directories.Any(UserContentDirectories.Contains)) score -= 40;

        // Explicit temp markers, cache paths, and bookkeeping files are decisive;
        // a MIME type or public parent folder must not cancel them.
        if (strongCachePath || definitiveName || temporaryName)
        {
            score = Math.Max(score, HideThreshold);
        }
        return score;
    }

    private static string GetFileName(string? name)
    {
        var normalized = (name ?? string.Empty).Trim().Replace('\\', '/');
        var separator = normalized.LastIndexOf('/');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    private static string GetStem(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    private static bool HasTemporaryNameMarker(string fileName)
    {
        for (var start = 0; start <= fileName.Length - 4;)
        {
            var marker = fileName.IndexOf("temp", start, StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return false;

            var markerLength = fileName.AsSpan(marker)
                .StartsWith("temporary", StringComparison.OrdinalIgnoreCase) ? 9 : 4;
            var end = marker + markerLength;
            var hasLeftBoundary = marker == 0 || !char.IsLetterOrDigit(fileName[marker - 1]) ||
                char.IsLower(fileName[marker - 1]) && char.IsUpper(fileName[marker]);
            var hasRightBoundary = end == fileName.Length || !char.IsLetterOrDigit(fileName[end]) ||
                char.IsDigit(fileName[end]) ||
                char.IsLower(fileName[end - 1]) && char.IsUpper(fileName[end]);
            if (hasLeftBoundary && hasRightBoundary) return true;

            start = marker + 4;
        }

        return false;
    }

    private static string[] GetDirectorySegments(string? path, string fileName)
    {
        var segments = (path ?? string.Empty)
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => segment.ToLowerInvariant())
            .ToList();
        if (segments.Count > 0 && string.Equals(segments[^1], fileName, StringComparison.OrdinalIgnoreCase))
        {
            segments.RemoveAt(segments.Count - 1);
        }
        return segments.ToArray();
    }

    private static bool ContainsAdjacentSegments(IReadOnlyList<string> segments, string first, string second)
    {
        for (var index = 0; index + 1 < segments.Count; index++)
        {
            if (segments[index] == first && segments[index + 1] == second) return true;
        }
        return false;
    }

    private static bool IsExtensionless(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot <= 0 || dot == fileName.Length - 1;
    }

    private static bool LooksLikeHexHash(string value) =>
        value.Length is >= 16 and <= 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool HasKnownMimeType(string? mimeType)
    {
        var value = mimeType?.Trim();
        return !string.IsNullOrEmpty(value) && value.Contains('/') &&
            !value.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("binary/octet-stream", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("unknown/unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAnySuffix(string value, params string[] suffixes) =>
        suffixes.Any(suffix => value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}
