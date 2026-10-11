namespace Hinge.Setup;

internal static class InstallerPathPolicy
{
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
