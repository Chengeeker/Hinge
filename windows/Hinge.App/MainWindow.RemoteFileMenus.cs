using Hinge.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Hinge.App;

public sealed partial class MainWindow
{
    // App-local cut/copy buffer: a content:// URI is meaningful only on the
    // phone which supplied it. Never paste that URI into another device.
    private RemoteFileEntry? _copiedPhoneFile;
    private string? _copiedPhoneFileDeviceId;

    private MenuFlyout BuildRemoteFileContextMenu(RemoteFileEntry entry)
    {
        var menu = new MenuFlyout();
        var canOpen = entry.IsDirectory || IsImageEntry(entry) ||
                      IsVideoEntry(entry) || IsAudioEntry(entry);
        var open = new MenuFlyoutItem
        {
            Text = entry.IsDirectory ? "打开文件夹" : "打开",
            IsEnabled = canOpen
        };
        open.Click += async (_, _) => await OpenRemoteFileFromMenuAsync(entry);
        menu.Items.Add(open);

        var copy = new MenuFlyoutItem
        {
            Text = "复制",
            IsEnabled = !entry.IsDirectory && !string.IsNullOrWhiteSpace(entry.Uri)
        };
        copy.Click += (_, _) =>
        {
            var source = GetFileConnection(entry);
            if (source?.IsSessionReady != true || string.IsNullOrWhiteSpace(source.RemoteDeviceId))
            {
                _filePage?.StatusText.SetValue(
                    Microsoft.UI.Xaml.Controls.TextBlock.TextProperty, "设备已断开，无法复制");
                return;
            }
            _copiedPhoneFile = entry;
            _copiedPhoneFileDeviceId = source.RemoteDeviceId;
            if (_filePage != null)
                _filePage.StatusText.Text = $"已复制 {entry.Name}，可粘贴到这台或其他已连接手机的文件夹";
        };
        menu.Items.Add(copy);

        var copyPath = new MenuFlyoutItem { Text = "复制完整路径" };
        copyPath.Click += (_, _) => CopyRemoteFilePath(entry);
        menu.Items.Add(copyPath);

        if (entry.IsDirectory && _fileCategory == "storage")
        {
            var pasteHere = new MenuFlyoutItem { Text = "粘贴到此文件夹" };
            pasteHere.Click += async (_, _) => await PasteRemotePhoneFileAsync(entry.RelativePath);
            menu.Items.Add(pasteHere);
            menu.Opening += (_, _) => pasteHere.IsEnabled = CanPasteRemotePhoneFile();
        }

        if (!entry.IsDirectory)
        {
            var save = new MenuFlyoutItem { Text = "保存到电脑" };
            save.Click += async (_, _) => await SaveSelectedFilesAsync(new[] { entry });
            menu.Items.Add(save);
            var delete = new MenuFlyoutItem { Text = "删除" };
            delete.Click += async (_, _) => await DeleteRemoteFileFromMenuAsync(entry);
            menu.Items.Add(delete);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var properties = new MenuFlyoutItem { Text = "属性" };
        properties.Click += async (_, _) => await ShowRemoteFilePropertiesAsync(entry);
        menu.Items.Add(properties);
        return menu;
    }

    private void ConfigureRemoteFileBackgroundMenu(FileManagementPage page)
    {
        page.Files.ContextFlyout = BuildRemoteFileBackgroundMenu();
        page.GridFiles.ContextFlyout = BuildRemoteFileBackgroundMenu();
    }

    private MenuFlyout BuildRemoteFileBackgroundMenu()
    {
        var menu = new MenuFlyout();
        var paste = new MenuFlyoutItem { Text = "粘贴到当前文件夹" };
        paste.Click += async (_, _) => await PasteRemotePhoneFileAsync(_filePath);
        menu.Items.Add(paste);
        menu.Opening += (_, _) =>
            paste.IsEnabled = _fileCategory == "storage" && CanPasteRemotePhoneFile();
        return menu;
    }

    private bool CanPasteRemotePhoneFile()
    {
        var source = _copiedPhoneFile;
        var sourceDevice = _copiedPhoneFileDeviceId;
        var targetDevice = _filePage?.SelectedPhoneDeviceId;
        return source is { IsDirectory: false } &&
            !string.IsNullOrWhiteSpace(source.Uri) &&
            !string.IsNullOrWhiteSpace(sourceDevice) &&
            !string.IsNullOrWhiteSpace(targetDevice) &&
            _sessionManager.ConnectionForDevice(sourceDevice)?.IsSessionReady == true &&
            _sessionManager.ConnectionForDevice(targetDevice)?.IsSessionReady == true;
    }

    private async Task PasteRemotePhoneFileAsync(string destinationRelativePath)
    {
        var source = _copiedPhoneFile;
        if (source == null || _fileCategory != "storage" || !CanPasteRemotePhoneFile())
        {
            await ShowDialogAsync("无法粘贴", "请先在已连接手机上复制文件，然后进入目标手机的“手机存储”文件夹。", false);
            return;
        }

        var targetId = _filePage?.SelectedPhoneDeviceId;
        if (string.IsNullOrWhiteSpace(targetId)) return;
        if (!string.Equals(targetId, _copiedPhoneFileDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            await PasteAcrossPhonesAsync(source, _copiedPhoneFileDeviceId!, targetId,
                destinationRelativePath);
            return;
        }

        var connection = _sessionManager.ConnectionForDevice(_copiedPhoneFileDeviceId!);
        if (connection?.IsSessionReady != true)
        {
            await ShowDialogAsync("无法粘贴", "源手机已经断开连接。", false);
            return;
        }

        try
        {
            if (_filePage != null) _filePage.StatusText.Text = $"正在复制 {source.Name}…";
            var actualPath = await _workspaceRemoteClient.CopyPhoneFileAsync(
                connection, source.Uri, source.Name, destinationRelativePath);
            if (_filePage != null)
                _filePage.StatusText.Text = $"已粘贴到 {PhoneStoragePath.Resolve(actualPath) ?? actualPath}";
            // Force MediaStore/storage cache refresh so the new file is visible.
            if (_filePage?.SelectedPhoneDeviceId == _copiedPhoneFileDeviceId &&
                _fileCategory == "storage")
                await RefreshRemoteFilesAsync(_fileCategory, _filePath, forceRefresh: true);
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("粘贴失败", exception.Message, false);
        }
    }

    private async Task OpenRemoteFileFromMenuAsync(RemoteFileEntry entry)
    {
        if (entry.IsDirectory && _fileCategory == "storage")
        {
            _filePath = entry.RelativePath;
            await RefreshRemoteFilesAsync(_fileCategory, _filePath);
        }
        else if (IsImageEntry(entry)) await ShowRemoteImagePreviewAsync(entry);
        else if (IsVideoEntry(entry)) await ShowRemoteVideoPreviewAsync(entry);
        else if (IsAudioEntry(entry)) await ShowRemoteAudioPreviewAsync(entry);
    }

    private static string? RemoteFileFullPath(RemoteFileEntry entry) =>
        PhoneStoragePath.Resolve(entry.RelativePath, entry.AbsolutePath,
            explicitStorageListing: entry.Category == "storage");

    private async Task ShowRemoteFilePropertiesAsync(RemoteFileEntry entry)
    {
        var fullPath = RemoteFileFullPath(entry);
        var directory = PhoneStoragePath.Directory(entry.RelativePath, entry.AbsolutePath,
            explicitStorageListing: entry.Category == "storage");
        var device = GetPhoneTargets().FirstOrDefault(target =>
            string.Equals(target.DeviceId, entry.SourceDeviceId, StringComparison.OrdinalIgnoreCase));
        var lines = new List<string>
        {
            $"名称：{entry.Name}",
            $"设备：{device?.Name ?? "当前手机"}",
            $"类型：{(entry.IsDirectory ? "文件夹" : entry.MimeType)}",
            $"大小：{(entry.IsDirectory ? "—" : FormatBytes(entry.SizeBytes) + $"（{entry.SizeBytes:N0} 字节）")}",
            $"修改时间：{FormatUnixMilliseconds(entry.ModifiedAt)}",
            $"完整路径：{fullPath ?? "手机未提供可确定的文件系统路径"}",
            $"所在目录：{directory ?? "手机未提供所在目录"}",
            $"来源分类：{entry.Category}"
        };
        if (fullPath == null)
            lines.Add("说明：此项仅返回了文件名或内容 URI，不能将内容 URI 当作文件系统目录。");
        await ShowDialogAsync($"属性 · {entry.Name}", string.Join("\n", lines), false);
    }

    private void CopyRemoteFilePath(RemoteFileEntry entry)
    {
        var full = RemoteFileFullPath(entry);
        if (string.IsNullOrWhiteSpace(full))
        {
            if (_filePage != null)
                _filePage.StatusText.Text = "手机没有返回这个文件的完整路径，可右键查看属性中的说明";
            return;
        }

        var package = new DataPackage();
        package.SetText(full);
        Clipboard.SetContent(package);
        Clipboard.Flush();
        if (_filePage != null) _filePage.StatusText.Text = $"已复制路径：{full}";
    }

    private async Task DeleteRemoteFileFromMenuAsync(RemoteFileEntry entry)
    {
        var connection = GetFileConnection(entry);
        if (connection?.IsSessionReady != true)
        {
            await ShowDialogAsync("无法删除", "手机连接已断开，请重新连接。", false);
            return;
        }

        if (!await ConfirmFileActionAsync(((FrameworkElement)Content).XamlRoot,
            "删除手机文件", $"确定永久删除手机上的“{entry.Name}”？此操作无法撤销。",
            "删除", destructive: true))
            return;
        try
        {
            var count = await _workspaceRemoteClient.DeleteFilesAsync(connection, new[] { entry.Uri });
            if (count < 1) throw new IOException("手机未能删除该文件，可能受到 Android 存储权限限制");
            await RefreshRemoteFilesAsync(_fileCategory, _filePath, forceRefresh: true);
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("删除失败", exception.Message, false);
        }
    }
}
