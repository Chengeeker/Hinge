namespace Hinge.Core;

public interface IScreenRenderer : IDisposable
{
    bool IsInitialized { get; }
    int CurrentWidth { get; }
    int CurrentHeight { get; }

    void Initialize(ScreenStreamConfig config);
    void RenderFrame(ScreenStreamPacket packet, ReadOnlySpan<byte> naluData);
    void Close();
}
