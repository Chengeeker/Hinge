namespace Hinge.Core;

public interface ITrayManager : IDisposable
{
    bool IsVisible { get; }
    void Initialize(string appName, string initialTooltip);
    void UpdateTooltip(string tooltip);
    void ShowNotification(string title, string text);
    event EventHandler? OpenRequested;
    event EventHandler? ExitRequested;
}
