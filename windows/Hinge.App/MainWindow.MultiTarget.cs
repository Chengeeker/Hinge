using Hinge.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hinge.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Explicit recipient picker for actions initiated without a device
    /// context (home button, tray and quick text). Never broadcast merely
    /// because multiple phones happen to be connected.
    /// </summary>
    private async Task<IReadOnlyList<SessionConnection>> SelectTargetSessionsAsync(
        string actionTitle)
    {
        var candidates = GetPhoneTargets(connectedOnly: true)
            .Select(phone => (Phone: phone,
                Connection: _sessionManager.ConnectionForDevice(phone.DeviceId)))
            .Where(pair => pair.Connection?.IsSessionReady == true)
            .ToArray();
        if (candidates.Length == 0)
        {
            await ShowDialogAsync("没有可用的手机",
                "请先在首页连接目标 Android 手机，然后重试。", false);
            return Array.Empty<SessionConnection>();
        }

        // A single connected phone is unambiguous only when it matches the
        // explicitly pinned workspace. If another phone is pinned but offline,
        // still ask the user before operating on the sole online alternative.
        if (candidates.Length == 1 &&
            (!_deviceWorkspace.HasExplicitSelection ||
             _deviceWorkspace.IsSelected(candidates[0].Phone.DeviceId)))
            return new[] { candidates[0].Connection! };

        var stack = new StackPanel { Spacing = 12, MinWidth = 240 };
        stack.Children.Add(new TextBlock
        {
            Text = "请选择接收设备。可以勾选多台手机；没有勾选的手机不会收到内容。",
            TextWrapping = TextWrapping.Wrap
        });
        var options = new List<(HandCursorCheckBox Check, SessionConnection Connection)>();
        foreach (var item in candidates)
        {
            var check = new HandCursorCheckBox
            {
                Content = item.Phone.Name + " · 已连接",
                IsChecked = _deviceWorkspace.IsSelected(item.Phone.DeviceId),
                MinHeight = 34
            };
            stack.Children.Add(check);
            options.Add((check, item.Connection!));
        }

        var dialog = new ContentDialog
        {
            Title = actionTitle + " · 选择手机",
            Content = stack,
            XamlRoot = ((FrameworkElement)Content).XamlRoot,
            PrimaryButtonText = "继续",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        void UpdatePrimary() =>
            dialog.IsPrimaryButtonEnabled = options.Any(option => option.Check.IsChecked == true);
        foreach (var item in options)
        {
            item.Check.Checked += (_, _) => UpdatePrimary();
            item.Check.Unchecked += (_, _) => UpdatePrimary();
        }
        UpdatePrimary();
        if (await ShowContentDialogAsync(dialog) != ContentDialogResult.Primary)
            return Array.Empty<SessionConnection>();

        return options.Where(option => option.Check.IsChecked == true)
            .Select(option => option.Connection)
            .Where(connection => connection.IsSessionReady)
            .Distinct()
            .ToArray();
    }
}
