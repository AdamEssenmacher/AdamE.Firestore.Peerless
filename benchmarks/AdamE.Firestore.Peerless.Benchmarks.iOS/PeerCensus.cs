using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// Counts <see cref="NSObject"/> peers by reading the runtime's own peer table: <c>ObjCRuntime.Runtime.object_map</c>,
/// the private <c>Dictionary&lt;IntPtr, GCHandle&gt;</c> every peer is registered in when it is created
/// (<c>Runtime.RegisterNSObject</c>) and removed from when it is disposed, finalized or its native object dies.
/// </summary>
/// <remarks>
/// Read through <see cref="UnsafeAccessorAttribute"/>, which NativeAOT compiles to a direct field access (no
/// reflection). It is private API of the Microsoft.iOS version this was built with (27.1.10018); if the field were
/// renamed the accessor would throw <see cref="MissingFieldException"/> on first use rather than miscount.
/// Two counts: <see cref="Count"/> is the table's entries. A collected peer's entry is removed only when
/// NSObject_Disposer drains on the main queue (or at once on explicit Dispose), so on the main queue the count is the
/// native retains held, not live managed peers, and its delta over a callback misses peers disposed within it. <see cref="Created"/> is the table's private <c>_version</c>, which .NET's
/// <c>Dictionary</c> increments on every insertion of a new key and never on removal or overwrite (since .NET Core
/// 3.0), and the runtime inserts a key exactly once per registered peer, so its delta is every peer created,
/// whatever was collected meanwhile.
/// </remarks>
internal static class PeerCensus
{
    private const string RuntimeType = "ObjCRuntime.Runtime, Microsoft.iOS";

    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "object_map")]
    private static extern ref Dictionary<nint, GCHandle> ObjectMap([UnsafeAccessorType(RuntimeType)] object? runtime);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_version")]
    private static extern ref int Version(Dictionary<nint, GCHandle> dictionary);

    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "lock_obj")]
    private static extern ref object LockObject([UnsafeAccessorType(RuntimeType)] object? runtime);

    /// <summary>Live entries in the peer table.</summary>
    public static int Count
    {
        get
        {
            lock (LockObject(null))
                return ObjectMap(null).Count;
        }
    }

    /// <summary>Peers registered since the runtime started (wraps after 2³¹, far beyond any run).</summary>
    public static int Created
    {
        get
        {
            lock (LockObject(null))
                return Version(ObjectMap(null));
        }
    }

    /// <summary>
    /// Walks the table: how many entries there are, how many still have a managed peer behind them, and the most common
    /// peer types (for example "4000 NSString").
    /// </summary>
    public static (int Entries, int Alive, string LargestKinds) Take(int kinds = 6)
    {
        var byType = new Dictionary<string, int>(StringComparer.Ordinal);
        int entries;
        int alive = 0;
        lock (LockObject(null))
        {
            Dictionary<nint, GCHandle> map = ObjectMap(null);
            entries = map.Count;
            foreach (GCHandle handle in map.Values)
            {
                if (handle.Target is not { } peer)
                    continue;
                alive++;
                string name = peer.GetType().Name;
                byType[name] = byType.GetValueOrDefault(name) + 1;
            }
        }

        string largest = string.Join("; ", byType
            .OrderByDescending(static pair => pair.Value)
            .Take(kinds)
            .Select(static pair => $"{pair.Value} {pair.Key}"));
        return (entries, alive, largest);
    }

    /// <summary>
    /// Checks the counter against peers whose number is known: creates 100 <see cref="NSObject"/>s, then disposes them.
    /// Returns the live-count deltas (should be +100, then −100) and the created-count deltas (+100, then +0).
    /// </summary>
    public static (int Live, int LiveAfterDispose, int Created, int CreatedAfterDispose) Calibrate()
    {
        int live = Count;
        int created = Created;
        var peers = new NSObject[100];
        for (int index = 0; index < peers.Length; index++)
            peers[index] = new NSObject();
        int liveDelta = Count - live;
        int createdDelta = Created - created;
        foreach (NSObject peer in peers)
            peer.Dispose();
        return (liveDelta, Count - live - liveDelta, createdDelta, Created - created - createdDelta);
    }
}
