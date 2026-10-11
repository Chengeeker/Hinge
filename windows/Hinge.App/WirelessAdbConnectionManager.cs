using System.Net.NetworkInformation;

namespace Hinge.App;

internal enum WirelessAdbConnectionState
{
    WaitingForDevice,
    WirelessDebuggingUnavailable,
    AuthorizationInvalid,
    NetworkUnavailable,
    DiscoveryFailed,
    ConnectionTimedOut,
    DeviceOffline,
    ConnectionFailed,
    Connected
}

internal sealed record WirelessAdbConnectionStatus(
    WirelessAdbConnectionState State, string Message, string? Transport = null);

internal sealed record WirelessAdbPairingResult(string Host, int Port, string? PairingGuid);

internal sealed record MdnsEndpoint(string Instance, string ServiceType, string Endpoint, string ServiceVersion);

internal sealed class WirelessAdbException(WirelessAdbConnectionState state, string message) : InvalidOperationException(message)
{
    public WirelessAdbConnectionState State { get; } = state;
}

internal sealed class WirelessAdbConnectionManager : IAsyncDisposable
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(30)
    ];

    private readonly Func<CancellationToken, Task<string>> _connect;
    private readonly Func<CancellationToken, Task<WirelessAdbRuntimeInfo>> _getRuntimeInfo;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly bool _listenForNetworkChanges;
    private readonly object _stateLock = new();
    private TaskCompletionSource<string> _transportAvailable = NewTransportSource();
    private Task? _runTask;
    private string? _transport;

    public event Action<WirelessAdbConnectionStatus>? StatusChanged;
    public string? CurrentTransport { get { lock (_stateLock) return _transport; } }

    internal WirelessAdbConnectionManager(
        Func<CancellationToken, Task<string>> connect,
        bool listenForNetworkChanges = true,
        Func<CancellationToken, Task<WirelessAdbRuntimeInfo>>? getRuntimeInfo = null)
    {
        _connect = connect;
        _listenForNetworkChanges = listenForNetworkChanges;
        _getRuntimeInfo = getRuntimeInfo ?? WirelessAdbClient.GetRuntimeInfoAsync;
    }

    public void Start()
    {
        if (_runTask != null) return;
        if (_listenForNetworkChanges)
        {
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }
        _runTask = RunAsync(_cancellation.Token);
    }

    public void NotifyNetworkChanged()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { }
    }

    public async Task<string> WaitForTransportAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task<string> wait;
            lock (_stateLock)
            {
                if (_transport != null) return _transport;
                wait = _transportAvailable.Task;
            }
            return await wait.WaitAsync(cancellationToken);
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        try
        {
            var runtime = await _getRuntimeInfo(cancellationToken);
            var wifi20 = runtime.SupportsAdbWifi20 ? "；检测到 ADB Wi-Fi 2.0" : string.Empty;
            Publish(new WirelessAdbConnectionStatus(WirelessAdbConnectionState.WaitingForDevice,
                $"Platform-Tools {runtime.PlatformToolsVersion}；ADB Server {runtime.ServerVersion}；mDNS {runtime.MdnsBackend}{wifi20}。"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            // The regular ADB connection attempt reports missing tools and server failures.
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            if (CurrentTransport == null)
                Publish(new WirelessAdbConnectionStatus(WirelessAdbConnectionState.WaitingForDevice,
                    "正在通过 Android Platform-Tools 恢复已配对设备…"));
            try
            {
                var transport = await _connect(cancellationToken);
                SetTransport(transport);
                failures = 0;
                Publish(new WirelessAdbConnectionStatus(WirelessAdbConnectionState.Connected,
                    "无线 ADB 已连接；已配对设备由 ADB 自动恢复。", transport));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                SetTransport(null);
                var state = exception is WirelessAdbException adbException
                    ? adbException.State
                    : WirelessAdbConnectionState.NetworkUnavailable;
                Publish(new WirelessAdbConnectionStatus(state,
                    exception.Message.Length == 0 ? "无线 ADB 暂不可用，正在重试。" : exception.Message));
            }

            var delay = CurrentTransport == null
                ? RetryDelays[Math.Min(failures++, RetryDelays.Length - 1)]
                : RetryDelays[^1];
            try { await _wake.WaitAsync(delay, cancellationToken); }
            catch (OperationCanceledException) { break; }
        }

        SetTransport(null);
        Publish(new WirelessAdbConnectionStatus(WirelessAdbConnectionState.WaitingForDevice, "无线 ADB 自动连接已停止。"));
    }

    private void SetTransport(string? transport)
    {
        lock (_stateLock)
        {
            _transport = transport;
            if (transport != null)
            {
                _transportAvailable.TrySetResult(transport);
            }
            else if (_transportAvailable.Task.IsCompleted)
            {
                _transportAvailable = NewTransportSource();
            }
        }
    }

    private void Publish(WirelessAdbConnectionStatus status)
    {
        try { StatusChanged?.Invoke(status); }
        catch { }
    }

    private void OnNetworkChanged(object? sender, EventArgs args) => NotifyNetworkChanged();
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args) => NotifyNetworkChanged();

    public async ValueTask DisposeAsync()
    {
        if (_listenForNetworkChanges)
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        }
        _cancellation.Cancel();
        NotifyNetworkChanged();
        if (_runTask != null)
        {
            try { await _runTask; }
            catch (OperationCanceledException) { }
        }
        _wake.Dispose();
        _cancellation.Dispose();
    }

    private static TaskCompletionSource<string> NewTransportSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
