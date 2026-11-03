using System.Diagnostics;
using Java.Interop;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>A point-in-time reading of everything a sample compares before and after.</summary>
internal readonly record struct ProbeReading(
    long Timestamp,
    long ThreadCpuNanos,
    long AllocatedBytes,
    int GlobalReferences,
    int WeakGlobalReferences,
    int Gen0,
    int Gen1,
    int Gen2,
    TimeSpan GcPause)
{
    public static ProbeReading Take() => new(
        Stopwatch.GetTimestamp(),
        Android.OS.Debug.ThreadCpuTimeNanos(),
        GC.GetAllocatedBytesForCurrentThread(),
        JniEnvironment.Runtime.GlobalReferenceCount,
        JniEnvironment.Runtime.WeakGlobalReferenceCount,
        GC.CollectionCount(0),
        GC.CollectionCount(1),
        GC.CollectionCount(2),
        GC.GetTotalPauseDuration());
}

internal static class Probes
{
    public static double Microseconds(long from, long to) =>
        (to - from) * 1_000_000.0 / Stopwatch.Frequency;

    public static bool IsMainThread => Android.OS.Looper.MyLooper() == Android.OS.Looper.MainLooper;

    /// <summary>
    /// Asks ART to log its own JNI reference tables, after a marker line, so the host can read the runtime's true
    /// global reference count. The .NET-side counter does not see every reference under NativeAOT.
    /// </summary>
    public static void DumpArtReferenceTables(string label)
    {
        Android.Util.Log.Info(BenchmarkRunner.Tag, $"ART_GREF_DUMP {label}");
        try
        {
            // A hidden API; the benchmark emulator allows it with `settings put global hidden_api_policy 1`.
            using Java.Lang.Class vmDebug = Java.Lang.Class.ForName("dalvik.system.VMDebug");
            using Java.Lang.Reflect.Method? dump = vmDebug.GetDeclaredMethod("dumpReferenceTables");
            _ = dump?.Invoke(null);
        }
        catch (Java.Lang.Exception exception)
        {
            Android.Util.Log.Warn(BenchmarkRunner.Tag, $"ART_GREF_DUMP unavailable: {exception.Message}");
        }
    }

    /// <summary>
    /// Collects .NET garbage, runs finalizers, asks ART to collect, then waits briefly so that peers whose Java
    /// objects are unreachable can release their global references. Returns the settled counts.
    /// </summary>
    public static async Task<(int Global, int WeakGlobal, int SurfacedPeers)> SettleAsync()
    {
        for (int pass = 0; pass < 3; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Java.Lang.JavaSystem.Gc();
            await Task.Delay(250).ConfigureAwait(false);
        }

        return (
            JniEnvironment.Runtime.GlobalReferenceCount,
            JniEnvironment.Runtime.WeakGlobalReferenceCount,
            JniEnvironment.Runtime.ValueManager.GetSurfacedPeers().Count);
    }
}
