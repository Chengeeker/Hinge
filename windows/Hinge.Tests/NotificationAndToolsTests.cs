using System.Text;
using Hinge.Core;
using Xunit;

namespace Hinge.Tests;

public class MockNotificationPresenter : INotificationPresenter
{
    public List<NotificationEventMessage> DisplayedNotifications { get; } = new();
    public List<string> DismissedNotificationIds { get; } = new();
    public event EventHandler<NotificationActionMessage>? ActionTriggered;

    public void ShowNotification(NotificationEventMessage notification)
    {
        DisplayedNotifications.Add(notification);
    }

    public void DismissNotification(string notificationId)
    {
        DismissedNotificationIds.Add(notificationId);
    }

    public void SimulateUserReply(string notificationId, string replyText)
    {
        ActionTriggered?.Invoke(this, new NotificationActionMessage
        {
            NotificationId = notificationId,
            ActionKey = "reply",
            ReplyText = replyText
        });
    }

    public void Dispose() { }
}

public class NotificationAndToolsTests
{
    [Fact]
    public void NotificationEventMessage_SerializationRoundtrip()
    {
        var msg = new NotificationEventMessage
        {
            NotificationId = "notif-001",
            PackageName = "com.slack",
            AppName = "Slack",
            Title = "Engineering Team",
            Content = "Release v1.0 ready for review",
            Source = "generic",
            Timestamp = 1725450000000L,
            CanReply = true,
            Actions = new List<string> { "reply", "dismiss" }
        };

        string json = msg.ToJson();
        var parsed = NotificationEventMessage.FromJson(json);

        Assert.NotNull(parsed);
        Assert.Equal("notif-001", parsed.NotificationId);
        Assert.Equal("com.slack", parsed.PackageName);
        Assert.Equal("Slack", parsed.AppName);
        Assert.Equal("Engineering Team", parsed.Title);
        Assert.Equal("Release v1.0 ready for review", parsed.Content);
        Assert.Equal("generic", parsed.Source);
        Assert.Equal(1725450000000L, parsed.Timestamp);
        Assert.True(parsed.CanReply);
        Assert.Equal(2, parsed.Actions.Count);
    }

    [Fact]
    public void NotificationActionMessage_SerializationRoundtrip()
    {
        var action = new NotificationActionMessage
        {
            NotificationId = "notif-001",
            ActionKey = "reply",
            ReplyText = "LGTM, proceeding!",
            Timestamp = 1725450005000L
        };

        string json = action.ToJson();
        var parsed = NotificationActionMessage.FromJson(json);

        Assert.NotNull(parsed);
        Assert.Equal("notif-001", parsed.NotificationId);
        Assert.Equal("reply", parsed.ActionKey);
        Assert.Equal("LGTM, proceeding!", parsed.ReplyText);
        Assert.Equal(1725450005000L, parsed.Timestamp);
    }

    [Fact]
    public void NotificationFilter_EnforcesWhitelistAndBlacklist()
    {
        var filter = new NotificationFilter();
        filter.BlacklistPackages.Add("com.spam.ads");

        var spamMsg = new NotificationEventMessage { PackageName = "com.spam.ads", Title = "Ad", Content = "Buy now" };
        var workMsg = new NotificationEventMessage { PackageName = "com.slack", Title = "Work", Content = "Standup" };

        Assert.False(filter.ShouldAllow(spamMsg));
        Assert.True(filter.ShouldAllow(workMsg));

        // Now set whitelist
        filter.WhitelistPackages.Add("com.slack");
        var workMsg2 = new NotificationEventMessage { PackageName = "com.slack", Title = "Work", Content = "Discussion" };
        var otherMsg = new NotificationEventMessage { PackageName = "com.gaming.app", Title = "Game", Content = "Turn ready" };

        Assert.True(filter.ShouldAllow(workMsg2));
        Assert.False(filter.ShouldAllow(otherMsg));
    }

    [Fact]
    public void NotificationFilter_DebouncesDuplicateSpam()
    {
        var filter = new NotificationFilter
        {
            DebounceWindow = TimeSpan.FromSeconds(2)
        };

        var msg1 = new NotificationEventMessage { PackageName = "com.chat", Title = "Chat", Content = "Hello" };
        var msg2 = new NotificationEventMessage { PackageName = "com.chat", Title = "Chat", Content = "Hello" };
        var msg3 = new NotificationEventMessage { PackageName = "com.chat", Title = "Chat", Content = "New message" };

        Assert.True(filter.ShouldAllow(msg1));
        // Immediate identical notification should be suppressed
        Assert.False(filter.ShouldAllow(msg2));
        // Different content should be allowed
        Assert.True(filter.ShouldAllow(msg3));
    }

    [Fact]
    public async Task NotificationManager_HandlesIncomingNotificationAndEnforcesTrust()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"trust_notif_{Guid.NewGuid():N}.json");
        try
        {
            var trustStore = new TrustStore(tempFile);
            string trustedDev = "dev-trusted-phone";
            trustStore.AddOrUpdate(new TrustedDevice { DeviceId = trustedDev, Name = "My Phone" });

            using var presenter = new MockNotificationPresenter();
            using var manager = new NotificationManager(presenter, trustStore);

            var notif = new NotificationEventMessage
            {
                PackageName = "com.work",
                AppName = "WorkApp",
                Title = "Meeting",
                Content = "Sprint sync starting in 5m"
            };

            var frame = new ProtocolFrame
            {
                Type = MessageType.NotificationEvent,
                Payload = Encoding.UTF8.GetBytes(notif.ToJson())
            };

            // Call with null conn (allowed in tests)
            bool handled = await manager.HandleIncomingFrameAsync(null!, frame);
            Assert.True(handled);
            Assert.Single(presenter.DisplayedNotifications);
            Assert.Equal("Sprint sync starting in 5m", presenter.DisplayedNotifications[0].Content);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task NotificationManager_ForwardsSmsVerificationCode()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"trust_sms_{Guid.NewGuid():N}.json");
        try
        {
            var trustStore = new TrustStore(tempFile);
            using var presenter = new MockNotificationPresenter();
            using var manager = new NotificationManager(presenter, trustStore);
            var notif = new NotificationEventMessage
            {
                NotificationId = "sms-001",
                PackageName = "android.provider.Telephony.SMS",
                AppName = "短信",
                Title = "1069",
                Content = "验证码 123456",
                Source = "sms",
                IsVerificationCode = true,
                VerificationCode = "123456",
                Actions = new List<string> { "copy_code" }
            };

            var frame = new ProtocolFrame
            {
                Type = MessageType.NotificationEvent,
                Payload = Encoding.UTF8.GetBytes(notif.ToJson())
            };

            Assert.True(await manager.HandleIncomingFrameAsync(null!, frame));
            Assert.Single(presenter.DisplayedNotifications);
            Assert.Equal("sms", presenter.DisplayedNotifications[0].Source);
            Assert.Equal("123456", presenter.DisplayedNotifications[0].VerificationCode);
            Assert.Contains("copy_code", presenter.DisplayedNotifications[0].Actions);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void PdfTools_CreateDocument_InspectMetadata()
    {
        var pages = new List<string> { "Page 1 Content", "Page 2 Content", "Page 3 Content" };
        byte[] pdfBytes = PdfTools.CreateDocument("Quarterly Report", pages);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 100);

        var meta = PdfTools.InspectMetadata(pdfBytes);
        Assert.Equal("1.4", meta.Version);
        Assert.Equal(3, meta.PageCount);
        Assert.True(meta.FileSizeBytes > 0);
    }

    [Fact]
    public void PdfTools_MergePdfs_CombinesDocuments()
    {
        byte[] doc1 = PdfTools.CreateDocument("Part 1", new[] { "P1", "P2" });
        byte[] doc2 = PdfTools.CreateDocument("Part 2", new[] { "P3", "P4" });

        byte[] merged = PdfTools.MergePdfs(new[] { doc1, doc2 }, "Merged Complete Document");
        var meta = PdfTools.InspectMetadata(merged);

        Assert.Equal(4, meta.PageCount);
        Assert.True(merged.Length > doc1.Length);
    }

    [Fact]
    public void PdfTools_SplitPdf_ExtractsSubRange()
    {
        byte[] doc = PdfTools.CreateDocument("Full Book", new[] { "P1", "P2", "P3", "P4", "P5" });
        byte[] split = PdfTools.SplitPdf(doc, 2, 4);

        var meta = PdfTools.InspectMetadata(split);
        Assert.Equal(3, meta.PageCount); // pages 2, 3, 4 = 3 pages
    }

    [Fact]
    public async Task OcrEngine_RecognizesImageText()
    {
        var engine = new MockOcrEngine();
        Assert.True(engine.IsAvailable);

        byte[] sampleImage = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }; // JPEG header
        var result = await engine.RecognizeTextAsync(sampleImage);

        Assert.True(result.Success);
        Assert.Contains("Hinge Native OCR", result.Text);
        Assert.Null(result.ErrorMessage);

        // Empty image
        var emptyResult = await engine.RecognizeTextAsync(Array.Empty<byte>());
        Assert.False(emptyResult.Success);
        Assert.NotNull(emptyResult.ErrorMessage);
    }
}
