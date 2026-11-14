using Hinge.Core;

namespace Hinge.Tests;

public sealed class NotificationRoutingPolicyTests
{
    [Fact]
    public void OnlySourcePhoneReceivesAnAction()
    {
        Assert.Equal(new[] { "OPPO" },
            NotificationRoutingPolicy.TargetIds(new[] { "vivo", "OPPO" }, "oppo"));
    }

    [Fact]
    public void UnscopedActionCannotBroadcastToTwoPhones()
    {
        Assert.Empty(NotificationRoutingPolicy.TargetIds(new[] { "vivo", "OPPO" }, null));
        Assert.Equal(new[] { "vivo" }, NotificationRoutingPolicy.TargetIds(
            new[] { "vivo", "VIVO" }, null));
    }

    [Fact]
    public void NotificationDedupeIsScopedPerPhone()
    {
        var filter = new NotificationFilter();
        var vivo = new NotificationEventMessage
        {
            SourceDeviceId = "vivo", PackageName = "com.demo",
            Title = "Hello", Content = "Same message"
        };
        var oppo = new NotificationEventMessage
        {
            SourceDeviceId = "oppo", PackageName = "com.demo",
            Title = "Hello", Content = "Same message"
        };
        Assert.True(filter.ShouldAllow(vivo));
        Assert.True(filter.ShouldAllow(oppo));
        Assert.False(filter.ShouldAllow(vivo));
    }
}
