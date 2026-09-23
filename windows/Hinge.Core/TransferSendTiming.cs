#if HINGE_TRANSFER_DIAGNOSTICS
using System.Diagnostics;

namespace Hinge.Core;

// Diagnostic build only. Times are cumulative elapsed waits, not CPU usage.
internal sealed class TransferSendTiming
{
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly string _transferId;
    private readonly long _fileSize;
    private readonly bool _streamingHash;
    private long _nextCheckpoint = 32L * 1024 * 1024;

    public long PrehashTicks;
    public long OfferTicks;
    public long AcceptWaitTicks;
    public long ReadTicks;
    public long HashTicks;
    public long QueueWaitTicks;
    public long FrameTicks;
    public long SendLockTicks;
    public long SocketWriteTicks;
    public long CompleteTicks;
    public long BytesSent;
    public long StartOffset;
    public string State = "starting";

    public TransferSendTiming(string transferId, long fileSize, bool streamingHash)
    {
        _transferId = transferId;
        _fileSize = fileSize;
        _streamingHash = streamingHash;
    }

    public void AddSend(long frameTicks, long lockTicks, long writeTicks)
    {
        FrameTicks += frameTicks;
        SendLockTicks += lockTicks;
        SocketWriteTicks += writeTicks;
    }

    public void Checkpoint()
    {
        if (BytesSent < _nextCheckpoint) return;
        _nextCheckpoint = BytesSent + 32L * 1024 * 1024;
        Write("progress");
    }

    public void Write(string phase)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hinge");
            Directory.CreateDirectory(directory);
            static long Ms(long ticks) => (long)Math.Round(
                ticks * 1000.0 / Stopwatch.Frequency);
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] " +
                $"transfer_send_timing transferId={_transferId} phase={phase} state={State} " +
                $"size={_fileSize} offset={StartOffset} bytes={BytesSent} streamingHash={_streamingHash} " +
                $"totalMs={_total.ElapsedMilliseconds} prehashMs={Ms(PrehashTicks)} " +
                $"offerMs={Ms(OfferTicks)} acceptWaitMs={Ms(AcceptWaitTicks)} " +
                $"readMs={Ms(Interlocked.Read(ref ReadTicks))} hashMs={Ms(Interlocked.Read(ref HashTicks))} " +
                $"queueWaitMs={Ms(Interlocked.Read(ref QueueWaitTicks))} " +
                $"frameMs={Ms(FrameTicks)} sendLockMs={Ms(SendLockTicks)} " +
                $"socketWriteMs={Ms(SocketWriteTicks)} completeMs={Ms(CompleteTicks)}";
            File.AppendAllText(Path.Combine(directory, "transfer-timing.log"),
                line + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must not change the transfer outcome.
        }
    }
}
#endif
