namespace Hinge.Setup;

internal static class InstallerPathPolicy
{
    public static bool IsInstalledDesktopHelper(string? executablePath, string installPath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(executablePath),
                Path.GetFullPath(Path.Combine(installPath, "Hinge.DesktopVideo.exe")), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static bool IsInsideSourceRepository(string path)
    {
        for (DirectoryInfo? directory = new(Path.GetFullPath(path)); directory != null; directory = directory.Parent)
        {
            var gitMarker = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker))
                return true;
        }

        return false;
    }
}
