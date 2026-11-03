using System.Diagnostics;
using System.Runtime.InteropServices;
using CoreFoundation;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>A point-in-time reading of everything a sample compares before and after.</summary>
internal readonly record struct ProbeReading(
    long Timestamp,
    long ThreadCpuNanos,
    long AllocatedBytes,
    int Peers,
    int PeersCreated,
    int Gen0,
    int Gen1,
    int Gen2,
    TimeSpan GcPause)
{
    public static ProbeReading Take() => new(
        Stopwatch.GetTimestamp(),
        Probes.ThreadCpuNanos(),
        GC.GetAllocatedBytesForCurrentThread(),
        PeerCensus.Count,
        PeerCensus.Created,
        GC.CollectionCount(0),
        GC.CollectionCount(1),
        GC.CollectionCount(2),
        GC.GetTotalPauseDuration());
}

/// <summary>What settling found: the peer table, and the managed heap.</summary>
internal readonly record struct SettledReading(int Peers, int AlivePeers, string LargestKinds, long HeapBytes);

internal static class Probes
{
    private const int ClockThreadCpuTimeId = 16; // CLOCK_THREAD_CPUTIME_ID in Darwin's <time.h>

    // Blittable, so a plain DllImport needs no marshalling stub (and no unsafe code for LibraryImport).
    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "clock_gettime_nsec_np")]
    private static extern ulong ClockGetTimeNanoseconds(int clock);

    /// <summary>CPU time the calling thread has used, in nanoseconds.</summary>
    public static long ThreadCpuNanos() => (long)ClockGetTimeNanoseconds(ClockThreadCpuTimeId);

    public static double Microseconds(long from, long to) => (to - from) * 1_000_000.0 / Stopwatch.Frequency;

    public static bool IsMainThread => NSThread.IsMain;

    /// <summary>
    /// Collects .NET garbage and runs finalizers (an <see cref="NSObject"/>'s finalizer releases its native object), then
    /// lets the main queue run, which drains its autorelease pool and processes releases queued for the main thread.
    /// Three passes, as on Android. Returns the settled counts.
    /// </summary>
    public static async Task<SettledReading> SettleAsync()
    {
        for (int pass = 0; pass < 3; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await OnMainQueueAsync(static () => { }).ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);
        }

        (int entries, int alive, string largest) = PeerCensus.Take();
        return new SettledReading(entries, alive, largest, GC.GetTotalMemory(forceFullCollection: false));
    }

    public static Task OnMainQueueAsync(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            try
            {
                action();
                done.TrySetResult();
            }
            catch (Exception exception)
            {
                done.TrySetException(exception);
            }
        });
        return done.Task;
    }
}
