using System.Net;
using Hinge.Core;

namespace Hinge.Tests;

/// <summary>Regression for simultaneous reverse/outbound handshakes.
/// If the preferred-direction socket does not exist, two callbacks used to
/// keep themselves and dispose one another, leaving no ready connection.</summary>
public sealed class SessionDuplicateRaceTests
{
    [Fact]
    public async Task CompetingInboundConnectionsWithSameDeviceIdKeepOneHealthyWinner()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var root = Path.Combine(Path.GetTempPath(),
                "hinge-dedup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var trust = new TrustStore(Path.Combine(root, "trust.json"));
                using var desktop = new SessionManager(
                    new DeviceIdentity { DeviceId = "a-desktop", Name = "PC" },
                    trust, 0);
                desktop.StartListener();
                using var phone1 = new SessionManager(
                    new DeviceIdentity { DeviceId = "z-phone", Name = "Phone" },
                    trust, 0);
                using var phone2 = new SessionManager(
                    new DeviceIdentity { DeviceId = "z-phone", Name = "Phone" },
                    trust, 0);

                // Both sockets arrive from Android. The PC prefers outbound
                // here (a-desktop < z-phone), so neither has the preferred
                // direction; the stable fallback must still keep one.
                var attempts = await Task.WhenAll(
                    phone1.ConnectToPeerAsync(IPAddress.Loopback, desktop.ListeningPort),
                    phone2.ConnectToPeerAsync(IPAddress.Loopback, desktop.ListeningPort));
                using var linkA = attempts[0];
                using var linkB = attempts[1];

                var timeout = DateTime.UtcNow.AddSeconds(4);
                while (desktop.ConnectionForDevice("z-phone") is null &&
                       DateTime.UtcNow < timeout)
                    await Task.Delay(20);

                await Task.Delay(350);
                var healthy = desktop.ActiveConnections
                    .Where(c => c.IsSessionReady && c.RemoteDeviceId == "z-phone")
                    .ToArray();
                Assert.Single(healthy);
                var winner = healthy[0];
                await Task.Delay(250);
                Assert.Same(winner, desktop.ConnectionForDevice("z-phone"));
                Assert.True(winner.IsSessionReady);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
