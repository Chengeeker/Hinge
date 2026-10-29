using Hinge.Setup;

namespace Hinge.Tests;

public class DesktopHelperPathTests
{
    [Theory]
    [InlineData(@"D:\Hinge\Hinge.DesktopVideo.exe", true)]
    [InlineData(@"d:\hinge\hinge.desktopvideo.EXE", true)]
    [InlineData(@"D:\Hinge-other\Hinge.DesktopVideo.exe", false)]
    [InlineData(@"D:\Hinge\tools\Hinge.DesktopVideo.exe", false)]
    [InlineData(@"D:\Hinge\..\Other\Hinge.DesktopVideo.exe", false)]
    [InlineData(@"D:\Hinge\QQ.exe", false)]
    [InlineData(null, false)]
    public void UpdateCanOnlyStopTheExactHelperInsideTheTargetInstall(string? executable, bool expected) =>
        Assert.Equal(expected, InstallerPathPolicy.IsInstalledDesktopHelper(executable, @"D:\Hinge"));
}
