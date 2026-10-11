using Hinge.Setup;

namespace Hinge.Tests;

public sealed class InstallerPathPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsPathsUnderGitWorktree(bool gitMarkerIsFile)
    {
        var root = Path.Combine(Path.GetTempPath(), "hinge-path-test-" + Guid.NewGuid().ToString("N"));
        var appPath = Path.Combine(root, "windows", "Hinge.App", "bin", "Debug");
        Directory.CreateDirectory(appPath);
        var marker = Path.Combine(root, ".git");
        if (gitMarkerIsFile) File.WriteAllText(marker, "gitdir: temp");
        else Directory.CreateDirectory(marker);

        try
        {
            Assert.True(InstallerPathPolicy.IsInsideSourceRepository(appPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AllowsStandaloneInstallDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "hinge-installed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            Assert.False(InstallerPathPolicy.IsInsideSourceRepository(path));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
