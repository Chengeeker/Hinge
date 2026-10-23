using Microsoft.UI.Xaml.Controls;

namespace Hinge.App;

public sealed partial class ScreenControlPage : Page
{
    public Button Back => BackButton;
    public Button Mirror => MirrorButton;
    public Button Control => ControlButton;
    public Button Stop => StopButton;
    public Button Pairing => PairingButton;
    public TextBlock Status => StatusText;
    public TextBlock FrameRate => FrameRateText;
    public ComboBox Codec => CodecOptions;
    public NumberBox MaxFps => MaxFpsBox;
    public NumberBox VideoBufferMs => VideoBufferMsBox;
    public NumberBox BitRate => BitRateBox;
    public NumberBox MaxSize => MaxSizeBox;
    public ToggleSwitch HardwareDecoding => HardwareDecodingToggle;

    public ScreenControlPage()
    {
        InitializeComponent();
        SettingsPageLayout.MatchViewportWidth(ScreenScrollViewer, ScreenPageRoot);
    }

    internal ScreenControlOptions Options
    {
        get
        {
            var options = new ScreenControlOptions((Codec.SelectedItem as ComboBoxItem)?.Tag as string ?? "h264",
                ReadInteger(MaxFps.Value), ReadInteger(BitRate.Value), ReadInteger(MaxSize.Value), HardwareDecoding.IsOn,
                ReadInteger(VideoBufferMs.Value));
            options.Validate();
            return options;
        }
    }

    private static int ReadInteger(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > int.MaxValue || value != Math.Truncate(value))
            throw new ArgumentException("请为画面参数填写有效整数。");
        return (int)value;
    }

    internal void SetOptions(ScreenControlOptions options)
    {
        Codec.SelectedItem = Codec.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == options.VideoCodec);
        MaxFps.Value = options.MaxFps;
        VideoBufferMs.Value = options.VideoBufferMs;
        BitRate.Value = options.BitRateMbps;
        MaxSize.Value = options.MaxSize;
        HardwareDecoding.IsOn = options.PreferHardwareDecoding;
    }
}
