namespace Hinge.Core;

public interface IDirectoryWatcher : IDisposable
{
    string WatchedPath { get; }
    bool IsWatching { get; }
    void Start();
    void Stop();
    event EventHandler<string>? Changed;
}
