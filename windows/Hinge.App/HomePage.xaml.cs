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
        Loaded += (_, _) =>
        {
            UpdateContentWidth();
            QueueTransferHistoryHeightUpdate();
            ApplyThemePalette();
        };
        ActualThemeChanged += (_, _) => ApplyThemePalette();
    }

    private void UpdateContentWidth()
    {
        var viewportWidth = HomeScrollViewer.ViewportWidth;
        if (viewportWidth > 0)
            HomePageContentRoot.Width = viewportWidth;
    }

    private void HomeScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateContentWidth();
        QueueTransferHistoryHeightUpdate();
    }

    private void QueueTransferHistoryHeightUpdate() =>
        DispatcherQueue.TryEnqueue(() =>
        {
            var viewportHeight = HomeScrollViewer.ViewportHeight;
            if (viewportHeight <= 0) return;

            var listTop = TransferHistoryListView
                .TransformToVisual(HomePageContentRoot)
                .TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            var reservedBottom = TransferHistoryCard.Padding.Bottom + HomeContentStack.Margin.Bottom;
            var availableHeight = Math.Max(120, viewportHeight - listTop - reservedBottom);
            if (Math.Abs(TransferHistoryListView.MaxHeight - availableHeight) > 1)
                TransferHistoryListView.MaxHeight = availableHeight;
        });

    public void ApplyThemePalette()
    {
        ActivityInfoBar.Background = ThemeBrushes.StatusSurface(this, ActivityInfoBar.Severity);
        ActivityInfoBar.Foreground = ThemeBrushes.StatusText(this, ActivityInfoBar.Severity);
    }

    private void HomeSummaryGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 900;
        Grid.SetColumn(IdentityCard, 0);
        Grid.SetRow(IdentityCard, 0);
        Grid.SetColumnSpan(IdentityCard, compact ? 2 : 1);
        Grid.SetColumn(ServiceCard, 0);
        Grid.SetRow(ServiceCard, 1);
        Grid.SetColumnSpan(ServiceCard, compact ? 2 : 1);
        Grid.SetColumn(NearbyDevicesCard, compact ? 0 : 1);
        Grid.SetRow(NearbyDevicesCard, compact ? 2 : 0);
        Grid.SetColumnSpan(NearbyDevicesCard, compact ? 2 : 1);
        Grid.SetRowSpan(NearbyDevicesCard, compact ? 1 : 2);
        QueueTransferHistoryHeightUpdate();
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
                Content = ThemeBrushes.Text(new TextBlock
                {
                    Text = "暂无传输记录。",
                    Padding = new Thickness(12, 16, 12, 16),
                    Foreground = ThemeBrushes.Secondary(this)
                })
            });
            QueueTransferHistoryHeightUpdate();
            return;
        }

        foreach (var record in records)
        {
            var row = new Grid
            {
                MinHeight = 64,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });

            var logo = MainWindow.BuildOfficialBrandLogo(string.Empty, record.DeviceName, 34, 24);
            if (logo is SymbolIcon fallbackIcon)
                fallbackIcon.Foreground = ThemeBrushes.AccentText(this);
            logo.HorizontalAlignment = HorizontalAlignment.Center;
            logo.VerticalAlignment = VerticalAlignment.Center;
            var logoSlot = new Grid
            {
                Width = 56,
                Height = 56,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { logo }
            };
            Grid.SetColumn(logoSlot, 0);
            row.Children.Add(logoSlot);

            var details = new StackPanel
            {
                Spacing = 3,
                Margin = new Thickness(12, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            details.Children.Add(new TextBlock
            {
                Text = $"{record.DirectionText} · {record.FileName}",
                Style = (Style)Application.Current.Resources["HingeM3RowTitleStyle"],
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            details.Children.Add(ThemeBrushes.Text(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(record.DeviceName)
                    ? $"{record.StatusText} · {record.TimeText}"
                    : $"{record.DeviceName} · {record.StatusText} · {record.TimeText}",
                Foreground = record.CanCancel
                    ? ThemeBrushes.AccentText(this)
                    : ThemeBrushes.Secondary(this),
                TextWrapping = TextWrapping.Wrap
            }, "HingeM3SupportingStyle"));
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
            Grid.SetColumn(details, 1);
            row.Children.Add(details);

            if (record.CanCancel || record.CanDelete)
            {
                var actionButton = new HandCursorButton
                {
                    Content = record.CanCancel ? "取消发送" : "删除",
                    Tag = record,
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    MinHeight = 36,
                    Style = (Style)Application.Current.Resources[
                        record.CanCancel ? "HingeM3TonalButtonStyle" : "HingeM3TextButtonStyle"]
                };
                if (record.CanCancel)
                {
                    actionButton.Click += TransferHistoryCancel_Click;
                }
                else
                {
                    actionButton.Click += TransferHistoryDelete_Click;
                }
                Grid.SetColumn(actionButton, 2);
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
        QueueTransferHistoryHeightUpdate();
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
