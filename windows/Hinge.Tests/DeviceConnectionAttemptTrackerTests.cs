using Hinge.Core;

namespace Hinge.Tests;

public sealed class DeviceConnectionAttemptTrackerTests
{
    [Fact]
    public void IndependentPhonesCanConnectAtSameTime()
    {
        var tracker = new DeviceConnectionAttemptTracker();
        Assert.True(tracker.TryBegin("phone-a", out var a));
        Assert.True(tracker.TryBegin("phone-b", out var b));
        Assert.True(tracker.IsCurrent("phone-a", a));
        Assert.True(tracker.IsCurrent("phone-b", b));
        Assert.False(tracker.TryBegin("phone-a", out _));
        tracker.End("phone-a", a);
        Assert.True(tracker.IsCurrent("phone-b", b));
        Assert.True(tracker.TryBegin("phone-a", out var retry));
        Assert.NotEqual(a, retry);
    }

    [Fact]
    public void CancellingOnePhoneDoesNotInvalidateAnother()
    {
        var tracker = new DeviceConnectionAttemptTracker();
        Assert.True(tracker.TryBegin("phone-a", out var a));
        Assert.True(tracker.TryBegin("phone-b", out var b));
        tracker.Cancel("PHONE-A");
        Assert.False(tracker.IsCurrent("phone-a", a));
        Assert.True(tracker.IsCurrent("phone-b", b));
        Assert.True(tracker.IsConnecting("phone-a"));
        tracker.End("phone-a", a);
        Assert.False(tracker.IsConnecting("phone-a"));
        Assert.True(tracker.IsConnecting("phone-b"));
        tracker.End("phone-b", b);
    }

    [Fact]
    public void CancelAllInvalidatesConcurrentAttempts()
    {
        var tracker = new DeviceConnectionAttemptTracker();
        Assert.True(tracker.TryBegin("phone-a", out var a));
        Assert.True(tracker.TryBegin("phone-b", out var b));
        tracker.CancelAll();
        Assert.False(tracker.IsCurrent("phone-a", a));
        Assert.False(tracker.IsCurrent("phone-b", b));
        tracker.End("phone-a", a);
        tracker.End("phone-b", b);
        Assert.True(tracker.TryBegin("phone-a", out var next));
        Assert.True(tracker.IsCurrent("phone-a", next));
    }
}
