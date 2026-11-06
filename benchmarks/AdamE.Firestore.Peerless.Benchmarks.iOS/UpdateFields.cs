using System.Runtime.InteropServices;
using ObjCRuntime;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// Builds an update's <c>{ version: number, updatedAt: timestamp }</c> dictionary with a peer only for the dictionary
/// itself, so the harness disposes nothing but peers it created.
/// </summary>
/// <remarks>
/// <para>
/// The peer table is keyed by native handle, and a small NSNumber or a short NSString is a tagged pointer: its handle is
/// its value, so the same number or string made anywhere in the process has the same handle. Getting the version or a
/// key through <c>NSNumber.FromInt64</c> or <c>new NSString</c> could therefore resolve to a peer already registered,
/// for example one a listener's conversion created that the GC hasn't collected yet, and disposing it would remove a
/// table entry the harness doesn't own (issue #37). Making the values non-tagged (a huge version number, say) would
/// change what the listener reads and converts, and so the measured numbers.
/// </para>
/// <para>
/// Instead the number and the keys are created natively, with no peer, and released once the dictionary retains them.
/// The dictionary is a new heap object, so the peer made for it is necessarily the harness's own. The native objects
/// written are the same as before (an NSNumber from a long long, two NSString keys, a two-entry NSDictionary).
/// </para>
/// </remarks>
internal static class UpdateFields
{
    private const string ObjectiveC = "/usr/lib/libobjc.dylib";

    private static readonly IntPtr NumberClass = Class.GetHandle("NSNumber");
    private static readonly IntPtr DictionaryClass = Class.GetHandle("NSDictionary");
    private static readonly IntPtr Alloc = Selector.GetHandle("alloc");
    private static readonly IntPtr InitWithLongLong = Selector.GetHandle("initWithLongLong:");
    private static readonly IntPtr InitWithObjectsForKeysCount = Selector.GetHandle("initWithObjects:forKeys:count:");

    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, long value);

    [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr[] objects, IntPtr[] keys, nuint count);

    [DllImport(ObjectiveC, EntryPoint = "objc_release")]
    private static extern void Release(IntPtr value);

    public static NSDictionary<NSObject, NSObject> Create(long version, NSObject updatedAt)
    {
        // Each +1 (alloc/init, CFStringCreate), released below; releasing a tagged pointer does nothing.
        IntPtr value = Send(Send(NumberClass, Alloc), InitWithLongLong, version);
        IntPtr versionKey = NSString.CreateNative("version");
        IntPtr updatedAtKey = NSString.CreateNative("updatedAt");
        try
        {
            IntPtr dictionary = Send(Send(DictionaryClass, Alloc), InitWithObjectsForKeysCount,
                [value, updatedAt.Handle], [versionKey, updatedAtKey], 2);
            // owns: true takes over the +1 from alloc/init, so disposing the peer releases it.
            return Runtime.GetNSObject<NSDictionary<NSObject, NSObject>>(dictionary, owns: true)
                ?? throw new InvalidOperationException("Could not create the update's fields.");
        }
        finally
        {
            Release(value);
            Release(versionKey);
            Release(updatedAtKey);
        }
    }
}
