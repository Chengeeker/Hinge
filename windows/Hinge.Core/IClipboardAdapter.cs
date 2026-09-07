namespace Hinge.Core;

/// <summary>
/// Platform abstraction for reading, writing, and monitoring the system clipboard.
/// </summary>
public interface IClipboardAdapter : IDisposable
{
    /// <summary>
    /// Gets the current plain text content of the clipboard.
    /// </summary>
    Task<string?> GetTextAsync();

    /// <summary>
    /// Sets the plain text content of the clipboard.
    /// </summary>
    Task SetTextAsync(string text);

    /// <summary>
    /// Starts monitoring the clipboard for changes.
    /// </summary>
    void StartMonitoring();

    /// <summary>
    /// Stops monitoring the clipboard.
    /// </summary>
    void StopMonitoring();

    /// <summary>
    /// Whether the adapter is actively monitoring.
    /// </summary>
    bool IsMonitoring { get; }

    /// <summary>
    /// Triggered when the clipboard text changes locally.
    /// </summary>
    event EventHandler<string>? TextChanged;
}
