using System.Text.Json;

namespace Hinge.Core;

public class SyncManager : IDisposable
{
    private readonly string _syncFolderPath;
    private readonly TransferManager _transferManager;
    private readonly IDirectoryWatcher? _watcher;
    private bool _disposed;

    public string SyncFolderPath => _syncFolderPath;
    public event EventHandler<SyncDifference>? SyncDifferenceComputed;
    public event EventHandler<string>? SyncCompleted;

    public SyncManager(
        string syncFolderPath,
        TransferManager transferManager,
        IDirectoryWatcher? watcher = null)
    {
        _syncFolderPath = syncFolderPath ?? throw new ArgumentNullException(nameof(syncFolderPath));
        _transferManager = transferManager ?? throw new ArgumentNullException(nameof(transferManager));
        _watcher = watcher;

        if (!Directory.Exists(_syncFolderPath))
        {
            Directory.CreateDirectory(_syncFolderPath);
        }

        if (_watcher != null)
        {
            _watcher.Changed += OnDirectoryChanged;
            _watcher.Start();
        }
    }

    private void OnDirectoryChanged(object? sender, string path)
    {
        // Directory change captured
    }

    public async Task RequestManifestAsync(SessionConnection conn, string folderId = "default")
    {
        var req = new SyncManifestMessage { FolderId = folderId };
        await conn.SendJsonAsync(MessageType.SyncManifestRequest, req);
    }

    public async Task SendManifestAsync(SessionConnection conn, string folderId = "default")
    {
        var manifest = await SyncIndexer.GenerateManifestAsync(_syncFolderPath, folderId);
        await conn.SendJsonAsync(MessageType.SyncManifestResponse, manifest);
    }

    public async Task<SyncDifference?> HandleIncomingFrameAsync(SessionConnection conn, ProtocolFrame frame)
    {
        switch (frame.Type)
        {
            case MessageType.SyncManifestRequest:
            {
                var req = JsonSerializer.Deserialize<SyncManifestMessage>(frame.Payload);
                await SendManifestAsync(conn, req?.FolderId ?? "default");
                return null;
            }
            case MessageType.SyncManifestResponse:
            {
                var remoteManifest = JsonSerializer.Deserialize<SyncManifestMessage>(frame.Payload);
                if (remoteManifest == null) return null;

                var localManifest = await SyncIndexer.GenerateManifestAsync(_syncFolderPath, remoteManifest.FolderId);
                var diff = SyncIndexer.CalculateDifference(localManifest, remoteManifest);

                SyncDifferenceComputed?.Invoke(this, diff);
                SyncCompleted?.Invoke(this, remoteManifest.FolderId);
                return diff;
            }
            case MessageType.SyncPullRequest:
            {
                var pullReq = JsonSerializer.Deserialize<SyncPullRequestMessage>(frame.Payload);
                if (pullReq != null)
                {
                    foreach (var rel in pullReq.RelativePaths)
                    {
                        string localFile = Path.Combine(_syncFolderPath, rel.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(localFile))
                        {
                            await _transferManager.SendFileAsync(conn, localFile);
                        }
                    }
                }
                return null;
            }
            default:
                return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_watcher != null)
        {
            _watcher.Changed -= OnDirectoryChanged;
            _watcher.Dispose();
        }
    }
}
