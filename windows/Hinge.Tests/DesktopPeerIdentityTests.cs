using Hinge.Core;

namespace Hinge.Tests;

public class DesktopPeerIdentityTests
{
    [Fact]
    public void PairingDisplayBindsBothCertificatesAndTheirRoles()
    {
        byte[] server = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        byte[] client = Enumerable.Range(64, 64).Select(i => (byte)i).ToArray();
        string code = DesktopPeerIdentity.VerificationCode(server, client);
        Assert.Matches("^[A-F0-9]{4}( [A-F0-9]{4}){3}$", code);
        Assert.Equal(code, DesktopPeerIdentity.VerificationCode(server.ToArray(), client.ToArray()));
        Assert.NotEqual(code, DesktopPeerIdentity.VerificationCode(client, server));
        client[0] ^= 1;
        Assert.NotEqual(code, DesktopPeerIdentity.VerificationCode(server, client));
        Assert.Throws<ArgumentException>(() => DesktopPeerIdentity.Fingerprint(new byte[16385]));
        Assert.Throws<ArgumentException>(() => DesktopPeerIdentity.Fingerprint([]));
    }
}
