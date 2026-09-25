using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Hinge.Core;

namespace Hinge.App;

public sealed partial class HomePage : Page
{
    public TextBlock HeroDeviceNameText => HeroDeviceName;
    public TextBlock HeroDeviceDetailText => HeroDeviceDetail;
    public ContentControl HeroDeviceLogoControl => HeroDeviceLogo;
    public TextBlock LocalDeviceInfoText => LocalDeviceInfo;
    public TextBlock DeviceCountTextBlock => DeviceCountText;
    public ListView DeviceList => DeviceListView;
    public InfoBar DiscoveryStatus => ActivityInfoBar;
    public TextBlock Status => StatusText;
    public Button Refresh => RefreshButton;
    public Button SendFile => SendFileButton;
    public Button ClearTransferHistory => ClearTransferHistoryButton;

    public event EventHandler<TransferHistoryRecord>? TransferCancelRequested;
    public event EventHandler<TransferHistoryRecord>? TransferHistoryDeleteRequested;
    public event EventHandler? TransferHistoryClearRequested;

    public HomePage()
    {
        InitializeComponent();
        ClearTransferHistoryButton.Click += ClearTransferHistoryButton_Click;
        Loaded += (_, _) => ApplyThemePalette();
        ActualThemeChanged += (_, _) => ApplyThemePalette();
    }

    public void ApplyThemePalette()
    {
        ActivityInfoBar.Background = ThemeBrushes.StatusSurface(this, ActivityInfoBar.Severity);
        ActivityInfoBar.Foreground = ThemeBrushes.StatusText(this, ActivityInfoBar.Severity);
    }

    public void SetTransferHistory(IReadOnlyList<TransferHistoryRecord> records)
    {
        ClearTransferHistoryButton.IsEnabled = records.Any(record => record.CanDelete);
        TransferHistoryListView.Items.Clear();
        if (records.Count == 0)
        {
            TransferHistoryListView.Items.Add(new ListViewItem
            {
                IsHitTestVisible = false,
                Content = new TextBlock
                {
                    Text = "暂无传输记录。",
                    Padding = new Thickness(12, 16, 12, 16),
                    Foreground = ThemeBrushes.Secondary(this)
                }
            });
            return;
        }

        foreach (var record in records)
        {
            var row = new Grid
            {
                MinHeight = 58,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });

            var details = new StackPanel
            {
                Spacing = 3,
                VerticalAlignment = VerticalAlignment.Center
            };
            details.Children.Add(new TextBlock
            {
                Text = $"{record.DirectionText} · {record.FileName}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            details.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(record.DeviceName)
                    ? $"{record.StatusText} · {record.TimeText}"
                    : $"{record.DeviceName} · {record.StatusText} · {record.TimeText}",
                Foreground = record.CanCancel
                    ? ThemeBrushes.AccentText(this)
                    : ThemeBrushes.Secondary(this),
                TextWrapping = TextWrapping.Wrap
            });
            if (record.State == TransferState.Transferring && record.TotalBytes > 0)
            {
                details.Children.Add(new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = Math.Clamp(
                        record.BytesTransferred / (double)record.TotalBytes * 100,
                        0,
                        100),
                    Height = 6,
                    MinWidth = 180,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 3, 0, 0),
                    IsHitTestVisible = false
                });
            }
            Grid.SetColumn(details, 0);
            row.Children.Add(details);

            if (record.CanCancel || record.CanDelete)
            {
                var actionButton = new Button
                {
                    Content = record.CanCancel ? "取消发送" : "删除",
                    Tag = record,
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Height = 36,
                    MinHeight = 36
                };
                if (record.CanCancel)
                {
                    actionButton.Click += TransferHistoryCancel_Click;
                }
                else
                {
                    actionButton.Click += TransferHistoryDelete_Click;
                }
                Grid.SetColumn(actionButton, 1);
                row.Children.Add(actionButton);
            }

            TransferHistoryListView.Items.Add(new ListViewItem
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
                Margin = new Thickness(0)
            });
        }
    }

    private void TransferHistoryCancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TransferHistoryRecord record })
        {
            TransferCancelRequested?.Invoke(this, record);
        }
    }

    private void TransferHistoryDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TransferHistoryRecord record })
        {
            TransferHistoryDeleteRequested?.Invoke(this, record);
        }
    }

    private void ClearTransferHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        TransferHistoryClearRequested?.Invoke(this, EventArgs.Empty);
    }
}
