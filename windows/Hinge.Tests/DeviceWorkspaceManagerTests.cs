using Hinge.Core;

namespace Hinge.Tests;

public class DeviceWorkspaceManagerTests
{
    [Fact]
    public void NewConnectionsNeverStealTheWorkspaceOrItsPerPageOverride()
    {
        var state = new DeviceWorkspaceManager();
        state.AdoptFirstConnected("vivo");
        state.SetPageOverride("files", "oppo");
        state.AdoptFirstConnected("third");
        Assert.Equal("vivo", state.SelectedDeviceId);
        Assert.Equal("oppo", state.TargetForPage("files"));
        Assert.Equal("vivo", state.TargetForPage("notes"));
        state.SelectDevice("third");
        Assert.Equal("third", state.TargetForPage("notes"));
        Assert.Equal("oppo", state.TargetForPage("files"));
    }

    [Fact]
    public void OfflineExplicitSelectionNeverFallsBackToAnotherPhone()
    {
        var state = new DeviceWorkspaceManager();
        state.RestoreSelection("offline-vivo");
        state.AdoptFirstConnected("online-oppo");
        Assert.Equal("offline-vivo", state.TargetForPage("photos"));
        Assert.True(state.HasExplicitSelection);
    }

    [Fact]
    public void SourceScopedIdsDoNotCollideAcrossPhones()
    {
        Assert.NotEqual(DeviceWorkspaceManager.CacheKey("vivo", "123"),
            DeviceWorkspaceManager.CacheKey("oppo", "123"));
        Assert.Equal(DeviceWorkspaceManager.CacheKey("VIVO", "abc"),
            DeviceWorkspaceManager.CacheKey("vivo", "abc"));
    }

    [Fact]
    public void ClearingPageOverrideRestoresDefaultDevice()
    {
        var state = new DeviceWorkspaceManager();
        state.SelectDevice("A");
        state.SetPageOverride("files", "B");
        state.ClearPageOverride("files");
        Assert.Equal("A", state.TargetForPage("files"));
    }
}
