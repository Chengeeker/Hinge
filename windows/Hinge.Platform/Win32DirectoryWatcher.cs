using Hinge.Core;

namespace Hinge.Platform;

public class Win32DirectoryWatcher : IDirectoryWatcher
{
    private readonly string _path;
    private readonly int _debounceMs;
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;
    private readonly object _lock = new();
    private bool _isWatching;
    private bool _disposed;

    public string WatchedPath => _path;
    public bool IsWatching => _isWatching;
    public event EventHandler<string>? Changed;

    public Win32DirectoryWatcher(string path, int debounceMs = 500)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _debounceMs = debounceMs;
    }

    public void Start()
    {
        if (_isWatching) return;

        if (!Directory.Exists(_path))
        {
            Directory.CreateDirectory(_path);
        }

        _watcher = new FileSystemWatcher(_path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Changed += OnFileSystemEvent;
        _watcher.Created += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
        _watcher.Renamed += OnRenamedEvent;

        _watcher.EnableRaisingEvents = true;
        _isWatching = true;
    }

    public void Stop()
    {
        _isWatching = false;
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        ScheduleDebounce(e.FullPath);
    }

    private void OnRenamedEvent(object sender, RenamedEventArgs e)
    {
        ScheduleDebounce(e.FullPath);
    }

    private void ScheduleDebounce(string fullPath)
    {
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(_ =>
            {
                Changed?.Invoke(this, fullPath);
            }, null, _debounceMs, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
