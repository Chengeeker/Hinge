using System.Collections.Concurrent;
using System.Net;
using Hinge.Core;

namespace Hinge.Tests;

public class MultiPhoneTests
{
    [Fact]
    public void HistoryCleanupRequiresConfirmedNameAndPreservesNewestAndProtectedIds()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "history-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "trust.json");
            var store = new TrustStore(path);
            foreach (var device in new[]
            {
                new TrustedDevice { DeviceId = "old", Name = "Same phone", LastSeen = 1 },
                new TrustedDevice { DeviceId = "connected", Name = "Same phone", LastSeen = 2 },
                new TrustedDevice { DeviceId = "queued", Name = "Same phone", LastSeen = 3 },
                new TrustedDevice { DeviceId = "newest", Name = "Same phone", LastSeen = 4 },
                new TrustedDevice { DeviceId = "other-old", Name = "Other phone", LastSeen = 1 },
                new TrustedDevice { DeviceId = "other-new", Name = "Other phone", LastSeen = 2 }
            }) store.AddOrUpdate(device);
            var protectedIds = new[] { "connected", "queued" };
            Assert.Equal(2, store.GetDuplicateHistory(protectedIds).Count);
            Assert.Equal(0, store.RemoveDuplicateHistory(Array.Empty<string>(), protectedIds).Removed);
            var result = store.RemoveDuplicateHistory(new[] { "Same phone" }, protectedIds);
            Assert.Equal(1, result.Removed);
            Assert.NotNull(result.BackupPath);
            Assert.Equal(6, new TrustStore(result.BackupPath).GetAllTrustedDevices().Count);
            var reloaded = new TrustStore(path);
            Assert.False(reloaded.IsTrusted("old"));
            foreach (var id in new[] { "connected", "queued", "newest", "other-old", "other-new" })
                Assert.True(reloaded.IsTrusted(id));
            Assert.Equal(0, store.RemoveDuplicateHistory(new[] { "Same phone" }, protectedIds).Removed);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void SendMenuKeepsConnectedPhonesAndOnlyLastOfflineTarget()
    {
        var devices = new[]
        {
            new PhoneDeviceTarget("old", "旧手机", false),
            new PhoneDeviceTarget("last", "最近手机", false),
            new PhoneDeviceTarget("a", "在线手机 A", true),
            new PhoneDeviceTarget("b", "在线手机 B", true)
        };
        var menu = PhoneDeviceTarget.ForSendMenu(devices, "last");
        Assert.Equal(3, menu.Count);
        Assert.DoesNotContain(menu, device => device.DeviceId == "old");
        Assert.Contains("离线", menu.Single(device => device.DeviceId == "last").DisplayName);
        Assert.Single(PhoneDeviceTarget.ForSendMenu(devices.Take(2), "last"));
        Assert.Equal(2, PhoneDeviceTarget.ForSendMenu(devices, "a").Count);
        Assert.Empty(PhoneDeviceTarget.ForSendMenu(devices.Take(2), null));
    }

    [Fact]
    public void ConnectedTargetsExcludeHistoryBeforeDisambiguatingNames()
    {
        var devices = new[]
        {
            new PhoneDeviceTarget("historical", "相同机型", false),
            new PhoneDeviceTarget("phone-a", "相同机型", true),
            new PhoneDeviceTarget("phone-b", "另一手机", true)
        };
        var connected = PhoneDeviceTarget.Merge(devices, connectedOnly: true);
        Assert.Equal(2, connected.Count);
        Assert.All(connected, device => Assert.True(device.IsConnected));
        Assert.Equal("相同机型", connected.Single(device => device.DeviceId == "phone-a").DisplayName);
        Assert.Empty(PhoneDeviceTarget.Merge(devices.Select(device => device with { IsConnected = false }), connectedOnly: true));
    }

    [Fact]
    public void TargetsKeepTwoSameModelPhonesAndPreferLiveIdentity()
    {
        var targets = PhoneDeviceTarget.Merge(new[]
        {
            new PhoneDeviceTarget("phone-a", "旧名称", false),
            new PhoneDeviceTarget("phone-a", "同一机型", true),
            new PhoneDeviceTarget("phone-b", "同一机型", true),
            new PhoneDeviceTarget("phone-c", "另一手机", false)
        });
        Assert.Equal(3, targets.Count);
        Assert.Equal(2, targets.Count(d => d.IsConnected));
        Assert.Equal(2, targets.Where(d => d.IsConnected).Select(d => d.DisplayName).Distinct().Count());
        Assert.Contains("离线", targets.Single(d => d.DeviceId == "phone-c").DisplayName);
        Assert.DoesNotContain(targets, d => d.Name == "旧名称");
    }

    [Fact]
    public async Task TwoPhonesTransferSameNameWithIndependentPreviewRoutesAndDisconnect()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "multi-phone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new TrustStore(Path.Combine(root, "trust.json"));
            using var server = new SessionManager(new DeviceIdentity { DeviceId = "computer", Name = "Computer" }, store, 0);
            using var receiver = new TransferManager(Path.Combine(root, "downloads"));
            var receipts = new ConcurrentDictionary<string, FileReceipt>();
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            receiver.FileReceivedFromDevice += (_, receipt) =>
            {
                receipts[receipt.DeviceId] = receipt;
                if (receipts.Count == 2) completed.TrySetResult();
            };
            server.MessageReceived += async (_, args) =>
            {
                try { await receiver.HandleIncomingFrameAsync(args.Connection, args.Frame); }
                catch (Exception error) { completed.TrySetException(error); }
            };
            server.StartListener();
            using var phoneA = new SessionManager(new DeviceIdentity { DeviceId = "phone-a", Name = "Same model" }, store, 0);
            using var phoneB = new SessionManager(new DeviceIdentity { DeviceId = "phone-b", Name = "Same model" }, store, 0);
            using var connectionA = await phoneA.ConnectToPeerAsync(IPAddress.Loopback, server.ListeningPort);
            using var connectionB = await phoneB.ConnectToPeerAsync(IPAddress.Loopback, server.ListeningPort);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((server.ConnectionForDevice("phone-a") == null || server.ConnectionForDevice("phone-b") == null) && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            Assert.NotNull(server.ConnectionForDevice("phone-a"));
            Assert.NotNull(server.ConnectionForDevice("phone-b"));
            Assert.Equal(2, server.ActiveConnections.Count);

            var sourceA = Path.Combine(root, "a"); var sourceB = Path.Combine(root, "b");
            Directory.CreateDirectory(sourceA); Directory.CreateDirectory(sourceB);
            var fileA = Path.Combine(sourceA, "same.txt"); var fileB = Path.Combine(sourceB, "same.txt");
            await File.WriteAllTextAsync(fileA, "From phone A"); await File.WriteAllTextAsync(fileB, "From phone B");
            var previewA = Path.Combine(root, "preview-a"); var previewB = Path.Combine(root, "preview-b");
            using var routeA = receiver.RegisterIncomingDirectory("same.txt", previewA, "phone-a");
            using var routeB = receiver.RegisterIncomingDirectory("same.txt", previewB, "phone-b");
            using var senderA = new TransferManager(Path.Combine(root, "send-a"));
            using var senderB = new TransferManager(Path.Combine(root, "send-b"));
            await Task.WhenAll(senderA.SendFileAsync(connectionA, fileA), senderB.SendFileAsync(connectionB, fileB));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(Path.Combine(previewA, "same.txt"), receipts["phone-a"].FilePath);
            Assert.Equal(Path.Combine(previewB, "same.txt"), receipts["phone-b"].FilePath);
            Assert.Equal("From phone A", await File.ReadAllTextAsync(receipts["phone-a"].FilePath));
            Assert.Equal("From phone B", await File.ReadAllTextAsync(receipts["phone-b"].FilePath));
            // Exercise the user's primary path too: one Windows sender to two phones.
            var receivedA = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var receivedB = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            senderA.FileReceived += (_, path) => receivedA.TrySetResult(path);
            senderB.FileReceived += (_, path) => receivedB.TrySetResult(path);
            phoneA.MessageReceived += async (_, args) =>
            {
                try { await senderA.HandleIncomingFrameAsync(args.Connection, args.Frame); }
                catch (Exception error) { receivedA.TrySetException(error); }
            };
            phoneB.MessageReceived += async (_, args) =>
            {
                try { await senderB.HandleIncomingFrameAsync(args.Connection, args.Frame); }
                catch (Exception error) { receivedB.TrySetException(error); }
            };
            await Task.WhenAll(
                receiver.SendFileAsync(server.ConnectionForDevice("phone-a")!, fileA),
                receiver.SendFileAsync(server.ConnectionForDevice("phone-b")!, fileB));
            await Task.WhenAll(receivedA.Task, receivedB.Task).WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal("From phone A", await File.ReadAllTextAsync(await receivedA.Task));
            Assert.Equal("From phone B", await File.ReadAllTextAsync(await receivedB.Task));
            server.ConnectionForDevice("phone-a")!.Dispose();
            Assert.NotNull(server.ConnectionForDevice("phone-b"));
            Assert.True(server.ConnectionForDevice("phone-b")!.IsSessionReady);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
