using System.IO.Pipes;
using System.Text;

namespace Hinge.App;

/// <summary>
/// Receives shell-send requests from the native Explorer command extension.
/// The pipe is intentionally scoped to the current Windows user so a second
/// user on the same machine cannot submit commands to this Hinge instance.
/// </summary>
internal sealed class ShellSendPipeServer : IDisposable
{
    internal const string PipeName = "Hinge.ShellSend.v1";
    private const int MaxPayloadCharacters = 1024 * 1024;

    private readonly Action<string> _onCommandLine;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _worker;
    private int _disposed;

    internal ShellSendPipeServer(Action<string> onCommandLine)
    {
        _onCommandLine = onCommandLine ?? throw new ArgumentNullException(nameof(onCommandLine));
        _worker = RunAsync();
    }

    private async Task RunAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                    0,
                    0);

                await pipe.WaitForConnectionAsync(_cancellation.Token).ConfigureAwait(false);
                using var reader = new StreamReader(
                    pipe,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 4096,
                    leaveOpen: false);
                var commandLine = await ReadBoundedCommandLineAsync(
                    reader,
                    _cancellation.Token).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(commandLine))
                {
                    _onCommandLine(commandLine.Trim());
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                break;
            }
            catch (IOException) when (_cancellation.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Explorer may retry while the app is starting or closing. Do
                // not let a transient pipe error terminate the resident app.
                try
                {
                    await Task.Delay(250, _cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private static async Task<string?> ReadBoundedCommandLineAsync(
        TextReader reader,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(capacity: 256);
        var buffer = new char[4096];

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (builder.Length + read > MaxPayloadCharacters) return null;
            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _cancellation.Cancel();
        _ = DisposeWorkerAsync();
        GC.SuppressFinalize(this);
    }

    private async Task DisposeWorkerAsync()
    {
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch
        {
            // Shutdown is best effort; the window is already closing.
        }
        finally
        {
            _cancellation.Dispose();
        }
    }
}
