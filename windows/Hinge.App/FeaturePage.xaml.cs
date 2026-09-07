using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hinge.App;

public sealed record FeaturePageOptions(
    string Title,
    string Description,
    string Status,
    string Glyph,
    string? PrimaryAction = null,
    string? SecondaryAction = null);

public sealed partial class FeaturePage : Page
{
    public Button? PrimaryActionButton { get; private set; }
    public Button? SecondaryActionButton { get; private set; }

    public FeaturePage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is FeaturePageOptions options) Configure(options);
    }

    private void Configure(FeaturePageOptions options)
    {
        FeatureIcon.Text = options.Glyph;
        FeatureDescription.Text = options.Description;
        FeatureStatus.Text = options.Status;
        FeatureActions.Children.Clear();
        PrimaryActionButton = null;
        SecondaryActionButton = null;

        if (!string.IsNullOrWhiteSpace(options.PrimaryAction))
        {
            PrimaryActionButton = new Button { Content = options.PrimaryAction };
            FeatureActions.Children.Add(PrimaryActionButton);
        }
        if (!string.IsNullOrWhiteSpace(options.SecondaryAction))
        {
            SecondaryActionButton = new Button { Content = options.SecondaryAction };
            FeatureActions.Children.Add(SecondaryActionButton);
        }
    }
}
