using Hinge.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hinge.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Transfer a file between Android devices by streaming through a private
    /// Windows staging directory. The Hinge TCP protocol already verifies
    /// incoming chunks and outbound hashes; no new Android RPC is required.
    /// </summary>
    private async Task PasteAcrossPhonesAsync(RemoteFileEntry source,
        string sourceId, string destinationId, string destinationPath)
    {
        var origin = _sessionManager.ConnectionForDevice(sourceId);
        var recipient = _sessionManager.ConnectionForDevice(destinationId);
        if (origin?.IsSessionReady != true || recipient?.IsSessionReady != true)
        {
            await ShowDialogAsync("手机连接已断开",
                "跨设备粘贴需要来源和目标手机同时保持连接。", false);
            return;
        }
        var targets = GetPhoneTargets();
        var originName = targets.FirstOrDefault(p => p.DeviceId == sourceId)?.Name ?? "来源手机";
        var targetName = targets.FirstOrDefault(p => p.DeviceId == destinationId)?.Name ?? "目标手机";
        var dialog = new ContentDialog
        {
            Title = "跨手机复制文件",
            Content = $"将“{source.Name}”从 {originName} 复制到 {targetName} 的\n" +
                      $"/storage/emulated/0/{destinationPath.Trim('/')}\n" +
                      "文件将通过 Windows 临时中转，成功后自动清除临时副本。" ,
            PrimaryButtonText = "复制到目标手机",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = ((FrameworkElement)Content).XamlRoot
        };
        if (await ShowContentDialogAsync(dialog) != ContentDialogResult.Primary) return;

        var stagingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hinge", "CrossPhoneStaging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        string? stagedFile = null;
        try
        {
            if (_filePage != null) _filePage.StatusText.Text =
                $"正在从 {originName} 接收 {source.Name}…";
            var actual = source.SourceDeviceId == sourceId
                ? source
                : new RemoteFileEntry
                {
                    Id = source.Id,
                    Uri = source.Uri,
                    Name = source.Name,
                    MimeType = source.MimeType,
                    RelativePath = source.RelativePath,
                    SizeBytes = source.SizeBytes,
                    SourceDeviceId = sourceId
                };
            stagedFile = await ReceiveRemoteFileAsync(actual,
                $"正在从 {originName} 接收 {source.Name}", stagingRoot);
            if (!File.Exists(stagedFile))
                throw new IOException("未收到手机文件的完整副本");
            if (_sessionManager.ConnectionForDevice(destinationId) is not { IsSessionReady: true } target)
                throw new IOException("目标手机传输前已断开连接");
            if (_filePage != null)
                _filePage.StatusText.Text = $"正在向 {targetName} 发送 {source.Name}…";
            var fileSize = new FileInfo(stagedFile).Length;
            await _transferManager.SendFileAsync(target, stagedFile,
                cancellationToken: CancellationToken.None,
                destinationPath: destinationPath);
            // Temporary receive records should not show files that no
            // longer exist after the staging directory is destroyed.
            RemoveTemporaryCrossPhoneReceipts(stagingRoot);
            _transferHistoryStore.Upsert(new TransferHistoryRecord
            {
                DeviceId = destinationId,
                DeviceName = $"{originName} → {targetName}",
                FileName = source.Name,
                FilePath = string.Empty,
                Direction = TransferDirection.Send,
                State = TransferState.Completed,
                BytesTransferred = fileSize,
                TotalBytes = fileSize,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            RefreshTransferHistory();
            if (_filePage != null)
                _filePage.StatusText.Text = $"已从 {originName} 复制到 {targetName}";
            if (_filePage?.SelectedPhoneDeviceId == destinationId)
                await RefreshRemoteFilesAsync(_fileCategory, _filePath, forceRefresh: true);
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("跨手机复制失败", exception.Message, false);
        }
        finally
        {
            RemoveTemporaryCrossPhoneReceipts(stagingRoot);
            try { Directory.Delete(stagingRoot, recursive: true); }
            catch { /* Best effort; old staging files can be pruned later. */ }
        }
    }

    private void RemoveTemporaryCrossPhoneReceipts(string stagingRoot)
    {
        foreach (var record in _transferHistoryStore.GetAll().Where(
            r => r.Direction == TransferDirection.Receive &&
                 !string.IsNullOrWhiteSpace(r.FilePath) &&
                 Path.GetFullPath(r.FilePath).StartsWith(
                     Path.GetFullPath(stagingRoot) + Path.DirectorySeparatorChar,
                     StringComparison.OrdinalIgnoreCase)).ToArray())
            _transferHistoryStore.Delete(record.Id);
    }
}
