using Hinge.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hinge.App;

public sealed partial class WebDavSettingsPage : Page
{
    private string _id = Guid.NewGuid().ToString("N");
    private int _loadGeneration;
    private WebDavProfile? _editingProfile;
    private bool _busy;
    private bool _passwordNeedsReplacement;
    public WebDavSettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (AddressEditor.Visibility == Visibility.Visible) ShowAddressList();
        else Frame.Navigate(typeof(SettingsPage));
    }
    private void ShowAddressList()
    {
        ++_loadGeneration;
        Password.Password = "";
        _passwordNeedsReplacement = false;
        _editingProfile = null;
        AddressEditor.Visibility = Visibility.Collapsed;
        AddressList.Visibility = Visibility.Visible;
        PageTitle.Text = "WebDAV";
        Status.Text = "";
        Reload();
    }
    private void Reload()
    {
        try
        {
            var profiles = WebDavSettingsStore.Load().Profiles;
            SavedAddresses.Children.Clear();
            foreach (var profile in profiles)
            {
                var button = new Button
                {
                    Content = CreateAddressCard(profile.DisplayName),
                    Padding = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                button.Click += (_, _) => EditAddress(profile);
                SavedAddresses.Children.Add(button);
            }
            if (profiles.Count == 0) SavedAddresses.Children.Add(new TextBlock { Text = "尚未添加 WebDAV 地址。" });
        }
        catch (Exception exception) { Status.Text = exception.Message; }
    }
    private static Grid CreateAddressCard(string name)
    {
        var grid = new Grid { Padding = new Thickness(14, 12, 14, 12), ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.Children.Add(new SymbolIcon(Symbol.Globe) { VerticalAlignment = VerticalAlignment.Center });
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = name, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = "编辑此地址的独立连接设置", Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        var arrow = new SymbolIcon(Symbol.Forward) { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(arrow, 2); grid.Children.Add(arrow);
        return grid;
    }
    private async void EditAddress(WebDavProfile profile)
    {
        if (_busy) return;
        _editingProfile = profile;
        _id = profile.Id;
        AddressList.Visibility = Visibility.Collapsed;
        AddressEditor.Visibility = Visibility.Visible;
        PageTitle.Text = "编辑 WebDAV 地址";
        Password.Password = "";
        Remark.Text = profile.Remark;
        Endpoint.Text = profile.Url;
        Username.Text = profile.Username;
        UserAgent.Text = profile.UserAgent;
        InitialPath.Text = profile.InitialPath;
        TrustCertificates.IsOn = profile.TrustAllCertificates;
        ParallelTransfers.IsOn = profile.ParallelTransfers;
        Status.Text = "";
        int generation = ++_loadGeneration;
        SetBusy(true);
        try
        {
            var password = await WebDavSettingsStore.UnprotectAsync(profile.ProtectedPassword);
            if (generation != _loadGeneration) return;
            Password.Password = password;
            Status.Text = "";
        }
        catch (Exception exception)
        {
            _passwordNeedsReplacement = true;
            Status.Text = exception.Message;
        }
        finally { if (generation == _loadGeneration) SetBusy(false); }
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        ++_loadGeneration;
        _editingProfile = null;
        _passwordNeedsReplacement = false;
        _id = Guid.NewGuid().ToString("N");
        AddressList.Visibility = Visibility.Collapsed;
        AddressEditor.Visibility = Visibility.Visible;
        PageTitle.Text = "新建 WebDAV 地址";
        Remark.Text = Endpoint.Text = Username.Text = UserAgent.Text = InitialPath.Text = "";
        Password.Password = "";
        TrustCertificates.IsOn = ParallelTransfers.IsOn = false;
        Status.Text = "填写后保存为新地址。";
        SetBusy(false);
    }
    private WebDavProfile ReadForm() => new()
    {
        Id = _id, Url = WebDavBrowserClient.ValidateEndpoint(Endpoint.Text).AbsoluteUri,
        Username = Username.Text, UserAgent = UserAgent.Text.Trim(), InitialPath = InitialPath.Text.Trim(),
        Remark = Remark.Text.Trim(), TrustAllCertificates = TrustCertificates.IsOn,
        ParallelTransfers = ParallelTransfers.IsOn
    };
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            if (_passwordNeedsReplacement && Password.Password.Length == 0)
                throw new InvalidOperationException("原密码无法解密，请重新填写密码后保存；原配置不会被覆盖。");
            var profile = ReadForm();
            using var validator = new WebDavBrowserClient(profile, Password.Password);
            if (profile.TrustAllCertificates)
            {
                var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "确认信任所有证书？",
                    Content = "这会跳过服务器身份验证，存在中间人攻击风险。仅对这个 WebDAV 地址生效。",
                    PrimaryButtonText = "确认启用", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }
            profile = profile with { ProtectedPassword = await WebDavSettingsStore.ProtectAsync(Password.Password) };
            WebDavSettingsStore.Upsert(profile);
            ShowAddressList();
            Status.Text = "已保存；文件管理中的标签已更新。密码由当前 Windows 用户账户保护。";
        }
        catch (Exception exception) { Status.Text = exception.Message; }
        finally { SetBusy(false); }
    }
    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            using var client = new WebDavBrowserClient(ReadForm(), Password.Password);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var entries = await client.ListAsync(client.InitialDirectory, timeout.Token);
            Status.Text = $"连接成功，初始目录包含 {entries.Count} 项。测试不会上传、删除文件或自动保存配置。";
        }
        catch (OperationCanceledException) { Status.Text = "连接测试超时。"; }
        catch (Exception exception) { Status.Text = exception.Message; }
        finally { SetBusy(false); }
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingProfile is not { } profile) return;
        SetBusy(true);
        try
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "删除 WebDAV 配置？",
                Content = $"只删除本机的“{profile.DisplayName}”配置，不删除服务器文件。",
                PrimaryButtonText = "删除配置", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            WebDavSettingsStore.Remove(profile.Id);
            ShowAddressList();
        }
        catch (Exception exception) { Status.Text = exception.Message; }
        finally { SetBusy(false); }
    }
    private void SetBusy(bool busy)
    {
        _busy = busy;
        AddressList.IsHitTestVisible = !busy;
        SaveButton.IsEnabled = TestButton.IsEnabled = !busy;
        DeleteButton.IsEnabled = !busy && _editingProfile != null;
        Remark.IsEnabled = Endpoint.IsEnabled = Username.IsEnabled = Password.IsEnabled = !busy;
        UserAgent.IsEnabled = InitialPath.IsEnabled = TrustCertificates.IsEnabled = ParallelTransfers.IsEnabled = !busy;
    }
}
