using Hinge.Core;

namespace Hinge.Platform;

public class Win32ScreenRenderer : IScreenRenderer
{
    private bool _isInitialized;
    private int _width;
    private int _height;
    private long _framesRendered;
    private bool _disposed;

    public bool IsInitialized => _isInitialized;
    public int CurrentWidth => _width;
    public int CurrentHeight => _height;
    public long TotalFramesRendered => _framesRendered;

    public void Initialize(ScreenStreamConfig config)
    {
        _width = config.Width;
        _height = config.Height;
        _framesRendered = 0;
        _isInitialized = true;
    }

    public void RenderFrame(ScreenStreamPacket packet, ReadOnlySpan<byte> naluData)
    {
        if (!_isInitialized) return;

        // Process H.264 Annex-B packet
        // Extract NAL unit type if start code exists
        if (naluData.Length >= 4)
        {
            int naluHeaderIndex = -1;
            if (naluData[0] == 0 && naluData[1] == 0 && naluData[2] == 0 && naluData[3] == 1)
            {
                naluHeaderIndex = 4;
            }
            else if (naluData[0] == 0 && naluData[1] == 0 && naluData[2] == 1)
            {
                naluHeaderIndex = 3;
            }

            if (naluHeaderIndex >= 0 && naluHeaderIndex < naluData.Length)
            {
                byte naluHeader = naluData[naluHeaderIndex];
                int nalType = naluHeader & 0x1F;
                // nalType: 7=SPS, 8=PPS, 5=IDR, 1=Coded slice
                _ = nalType;
            }
        }

        _framesRendered++;
    }

    public void Close()
    {
        _isInitialized = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }
}
