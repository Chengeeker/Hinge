using Hinge.Core;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace Hinge.Platform;

/// <summary>
/// Publishes a short manufacturer-data BLE advertisement. Windows desktop
/// Bluetooth advertising is best-effort; callers must retain the LAN queue and
/// provide the notification/manual-wake fallback when publishing is unavailable.
/// </summary>
public sealed class BluetoothWakeAdvertiser : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _shutdownToken;
    private bool _disposed;

    public BluetoothWakeAdvertiser() => _shutdownToken = _shutdown.Token;

    public async Task<bool> PublishAsync(
        string deviceId,
        WakeAdvertisementKind kind,
        TimeSpan duration,
        CancellationToken cancellationToken,
        Action? onStarted = null,
        bool skipIfBusy = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (duration <= TimeSpan.Zero) return false;

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownToken);
        cancellationToken = lifetime.Token;
        if (skipIfBusy)
        {
            if (!await _gate.WaitAsync(0, cancellationToken)) return false;
        }
        else
        {
            await _gate.WaitAsync(cancellationToken);
        }
        BluetoothLEAdvertisementPublisher? publisher = null;
        TypedEventHandler<BluetoothLEAdvertisementPublisher, BluetoothLEAdvertisementPublisherStatusChangedEventArgs>? statusHandler = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            publisher = new BluetoothLEAdvertisementPublisher();
            IBuffer buffer;
            using (var writer = new DataWriter())
            {
                writer.WriteBytes(HingeWakeProtocol.CreatePayload(deviceId, kind));
                buffer = writer.DetachBuffer();
            }
            var manufacturerData = new BluetoothLEManufacturerData(
                HingeWakeProtocol.CompanyId,
                buffer);
            publisher.Advertisement.ManufacturerData.Add(manufacturerData);

            var started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            statusHandler = (_, args) =>
            {
                switch (args.Status)
                {
                    case BluetoothLEAdvertisementPublisherStatus.Started:
                        started.TrySetResult(true);
                        break;
                    case BluetoothLEAdvertisementPublisherStatus.Aborted:
                    case BluetoothLEAdvertisementPublisherStatus.Stopped:
                        started.TrySetResult(false);
                        break;
                }
            };
            publisher.StatusChanged += statusHandler;
            publisher.Start();

            using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupTimeout.CancelAfter(TimeSpan.FromSeconds(2));
            bool running;
            try
            {
                running = await started.Task.WaitAsync(startupTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                running = publisher.Status == BluetoothLEAdvertisementPublisherStatus.Started;
            }

            if (!running || publisher.Status != BluetoothLEAdvertisementPublisherStatus.Started) return false;
            onStarted?.Invoke();
            await Task.Delay(duration, cancellationToken);
            return publisher.Status == BluetoothLEAdvertisementPublisherStatus.Started;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            // Bluetooth may be disabled, unavailable on the host, or blocked
            // by the current package/capability identity. The caller's queue
            // and manual wake path are the required fallback.
            return false;
        }
        finally
        {
            if (publisher != null)
            {
                try
                {
                    if (publisher.Status is BluetoothLEAdvertisementPublisherStatus.Started or
                        BluetoothLEAdvertisementPublisherStatus.Waiting)
                    {
                        publisher.Stop();
                    }
                }
                catch
                {
                    // Best-effort cleanup; WinRT owns the publisher lifetime.
                }

                if (statusHandler != null)
                {
                    publisher.StatusChanged -= statusHandler;
                }
            }
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        _shutdown.Dispose();
        // In-flight publishers release the gate as they stop.
    }
}
