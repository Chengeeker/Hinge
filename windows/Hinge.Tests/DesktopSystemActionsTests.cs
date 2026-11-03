using Hinge.App;
using Hinge.Core;

namespace Hinge.Tests;

public class DesktopSystemActionsTests
{
    [Theory]
    [InlineData(DesktopSystemAction.Restart, "/r")]
    [InlineData(DesktopSystemAction.Shutdown, "/s")]
    public void PowerArgumentsAreFixedAndNeverForceCloseApps(DesktopSystemAction action, string flag)
    {
        // Inspect the real command construction; never execute power operations during tests.
        var info = DesktopSystemActions.ShutdownStartInfo(action);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe"), info.FileName);
        Assert.Equal(new[] { flag, "/t", "0" }, info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.Throws<ArgumentOutOfRangeException>(() => DesktopSystemActions.ShutdownStartInfo((DesktopSystemAction)255));
        Assert.Throws<ArgumentOutOfRangeException>(() => DesktopSystemActions.ShutdownStartInfo(DesktopSystemAction.Lock));
    }
}
