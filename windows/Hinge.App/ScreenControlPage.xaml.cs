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
    public Slider MaxFps => MaxFpsBox;
    public Slider VideoBufferMs => VideoBufferMsBox;
    public Slider BitRate => BitRateBox;
    public Slider MaxSize => MaxSizeBox;
    public ToggleSwitch HardwareDecoding => HardwareDecodingToggle;

    public ScreenControlPage()
    {
        InitializeComponent();
        foreach (var slider in new[] { MaxFps, VideoBufferMs, BitRate, MaxSize })
            slider.ValueChanged += (_, _) => UpdateParameterLabels();
        UpdateParameterLabels();
        SettingsPageLayout.MatchViewportWidth(ScreenScrollViewer, ScreenPageRoot);
    }

    private void UpdateParameterLabels()
    {
        MaxFpsBoxLabel.Text = $"帧率上限 · {MaxFps.Value:0} FPS";
        VideoBufferMsBoxLabel.Text = $"平滑缓冲 · {VideoBufferMs.Value:0} 毫秒";
        BitRateBoxLabel.Text = $"视频码率 · {BitRate.Value:0} Mbps";
        MaxSizeBoxLabel.Text = MaxSize.Value == 0 ? "画面最长边 · 原始分辨率" : $"画面最长边 · {MaxSize.Value:0} 像素";
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
            throw new ArgumentException("请选择有效的画面参数。");
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
