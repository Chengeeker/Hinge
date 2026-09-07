using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public class TransferTests
{
    [Fact]
    public async Task TransferManager_SendText_Dispatches_TextReceived()
    {
        int port = 52880;
        var store = new TrustStore(Path.Combine(Path.GetTempPath(), $"ts_{Guid.NewGuid()}.json"));
        var serverId = new DeviceIdentity { DeviceId = "server", Name = "Server" };
        using var serverSession = new SessionManager(serverId, store, port);
        serverSession.StartListener();

        string tempDir = Path.Combine(Path.GetTempPath(), $"trans_{Guid.NewGuid()}");
        using var receiverTransfer = new TransferManager(tempDir);
        var textTcs = new TaskCompletionSource<TextTransferMessage>();
        receiverTransfer.TextReceived += (s, msg) => textTcs.TrySetResult(msg);

        serverSession.MessageReceived += async (s, e) =>
        {
            await receiverTransfer.HandleIncomingFrameAsync(e.Connection, e.Frame);
        };

        var clientId = new DeviceIdentity { DeviceId = "client", Name = "Client" };
        using var clientSession = new SessionManager(clientId, store, port + 1);
        using var clientConn = await clientSession.ConnectToPeerAsync(IPAddress.Loopback, port);

        using var senderTransfer = new TransferManager();
        await senderTransfer.SendTextAsync(clientConn, "Hello from Android to Windows 🚀", "text");

        var received = await Task.WhenAny(textTcs.Task, Task.Delay(3000));
        Assert.Equal(textTcs.Task, received);

        var msg = await textTcs.Task;
        Assert.Equal("text", msg.Type);
        Assert.Equal("Hello from Android to Windows 🚀", msg.Content);
    }

    [Fact]
    public async Task TransferManager_StreamFile_Verifies_Sha256_And_Saves_File()
    {
        int port = 52885;
        var store = new TrustStore(Path.Combine(Path.GetTempPath(), $"ts_{Guid.NewGuid()}.json"));
        var serverId = new DeviceIdentity { DeviceId = "server-file", Name = "Server" };
        using var serverSession = new SessionManager(serverId, store, port);
        serverSession.StartListener();

        string downloadDir = Path.Combine(Path.GetTempPath(), $"dl_{Guid.NewGuid()}");
        using var receiverTransfer = new TransferManager(downloadDir);

        serverSession.MessageReceived += async (s, e) =>
        {
            await receiverTransfer.HandleIncomingFrameAsync(e.Connection, e.Frame);
        };

        var clientId = new DeviceIdentity { DeviceId = "client-file", Name = "Client" };
        using var clientSession = new SessionManager(clientId, store, port + 1);
        using var clientConn = await clientSession.ConnectToPeerAsync(IPAddress.Loopback, port);

        using var senderTransfer = new TransferManager();

        // Create a test file of 128KB (spanning multiple 64KB chunks)
        string testFile = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid()}.bin");
        byte[] testBytes = new byte[128 * 1024];
        RandomNumberGenerator.Fill(testBytes);
        File.WriteAllBytes(testFile, testBytes);

        try
        {
            var fileReceivedTcs = new TaskCompletionSource<string>();
            receiverTransfer.FileReceived += (s, path) => fileReceivedTcs.TrySetResult(path);

            // Send file
            string transferId = await senderTransfer.SendFileAsync(clientConn, testFile);
            Assert.False(string.IsNullOrEmpty(transferId));

            var completed = await Task.WhenAny(fileReceivedTcs.Task, Task.Delay(5000));
            Assert.Equal(fileReceivedTcs.Task, completed);

            string receivedPath = await fileReceivedTcs.Task;
            Assert.True(File.Exists(receivedPath));

            byte[] receivedBytes = File.ReadAllBytes(receivedPath);
            Assert.Equal(testBytes.Length, receivedBytes.Length);
            Assert.Equal(testBytes, receivedBytes);
        }
        finally
        {
            if (File.Exists(testFile)) File.Delete(testFile);
            if (Directory.Exists(downloadDir)) Directory.Delete(downloadDir, true);
        }
    }

    [Fact]
    public async Task TransferManager_ConcurrentSameNameTransfers_DoNotSharePartialFile()
    {
        int port = 52890;
        var store = new TrustStore(Path.Combine(Path.GetTempPath(), $"ts_{Guid.NewGuid()}.json"));
        var serverId = new DeviceIdentity { DeviceId = "server-concurrent", Name = "Server" };
        using var serverSession = new SessionManager(serverId, store, port);
        serverSession.StartListener();

        string downloadDir = Path.Combine(Path.GetTempPath(), $"dl_{Guid.NewGuid()}");
        using var receiverTransfer = new TransferManager(downloadDir);
        var receivedCount = 0;
        var receivedTcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        receiverTransfer.FileReceived += (_, _) =>
        {
            if (Interlocked.Increment(ref receivedCount) >= 2)
            {
                receivedTcs.TrySetResult(true);
            }
        };

        serverSession.MessageReceived += async (_, args) =>
        {
            await receiverTransfer.HandleIncomingFrameAsync(args.Connection, args.Frame);
        };

        var clientId = new DeviceIdentity { DeviceId = "client-concurrent", Name = "Client" };
        using var clientSession = new SessionManager(clientId, store, port + 1);
        using var clientConn = await clientSession.ConnectToPeerAsync(IPAddress.Loopback, port);
        using var senderTransfer = new TransferManager();

        string sourceDirectory = Path.Combine(Path.GetTempPath(), $"src_{Guid.NewGuid()}");
        string firstFile = Path.Combine(sourceDirectory, "same-name.jpg");
        string secondFile = Path.Combine(sourceDirectory, "nested", "same-name.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(secondFile)!);
        byte[] firstBytes = new byte[128 * 1024];
        byte[] secondBytes = new byte[128 * 1024];
        RandomNumberGenerator.Fill(firstBytes);
        RandomNumberGenerator.Fill(secondBytes);
        File.WriteAllBytes(firstFile, firstBytes);
        File.WriteAllBytes(secondFile, secondBytes);

        try
        {
            await Task.WhenAll(
                senderTransfer.SendFileAsync(clientConn, firstFile),
                senderTransfer.SendFileAsync(clientConn, secondFile));

            var completed = await Task.WhenAny(receivedTcs.Task, Task.Delay(10000));
            Assert.Equal(receivedTcs.Task, completed);
            string receivedPath = Path.Combine(downloadDir, "same-name.jpg");
            Assert.True(File.Exists(receivedPath));
            byte[] receivedBytes = File.ReadAllBytes(receivedPath);
            Assert.True(
                receivedBytes.SequenceEqual(firstBytes) ||
                receivedBytes.SequenceEqual(secondBytes));
        }
        finally
        {
            if (Directory.Exists(sourceDirectory)) Directory.Delete(sourceDirectory, true);
            if (Directory.Exists(downloadDir)) Directory.Delete(downloadDir, true);
        }
    }
}
