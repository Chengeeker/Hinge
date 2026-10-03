namespace Hinge.Core;

/// <summary>Maps DAV-root-relative names to an explicitly configured, existing mount.</summary>
public static class WebDavThumbnailPath
{
    public static string? Resolve(Uri root, Uri file, string mountPath)
    {
        if (string.IsNullOrWhiteSpace(mountPath)) return null;
        if (!Path.IsPathFullyQualified(mountPath)) throw new ArgumentException("挂载目录必须是完整路径，例如 Z:\\ 或 Z:\\网盘。 ");
        if (root.Scheme != file.Scheme || root.Authority != file.Authority ||
            !file.AbsolutePath.StartsWith(root.AbsolutePath, StringComparison.Ordinal)) return null;
        var parts = file.AbsolutePath[root.AbsolutePath.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString).ToArray();
        // Do not let escaped separators, alternate streams or Windows-normalized names leave the mount.
        if (parts.Length == 0 || parts.Any(p => p is "." or ".." ||
            p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.Contains('\\') || p.EndsWith('.') || p.EndsWith(' '))) return null;
        var directory = Path.GetFullPath(mountPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(directory, Path.Combine(parts)));
        return result.StartsWith(directory, StringComparison.OrdinalIgnoreCase) ? result : null;
    }
}
