using System.Runtime.InteropServices;

namespace AdamE.Firestore.Peerless.iOS;

/// <summary>
/// The measurement the shim delivers with a timed frame (ios/PeerlessShim/include/PeerlessShim.h). The callback receives
/// it as a <c>const</c> pointer into the shim's stack, valid only during the call: copy it, never write through it.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FrameTiming
{
    public ulong StartNanos;
    public ulong StartCpuNanos;
    public ulong EncodeNanos;
    public ulong EncodeCpuNanos;
    public ulong DataNanos;
}

/// <summary>
/// The shim's C API, statically linked into the app (hence <c>__Internal</c>). Every signature is blittable and the
/// callbacks are unmanaged function pointers, so NativeAOT calls them directly: no marshalling stubs, no registrar.
/// </summary>
internal static unsafe class Native
{
    private const string Library = "__Internal";

    /// <summary>The shim's error code for a snapshot it could not encode.</summary>
    public const int EncodeFailed = -1;

    [DllImport(Library, EntryPoint = "adame_peerless_listen_query")]
    public static extern nint ListenQuery(
        nint query,
        nint context,
        delegate* unmanaged<nint, byte*, nuint, FrameTiming*, void> onFrame,
        delegate* unmanaged<nint, int, byte*, void> onError,
        int timed,
        byte* error,
        nuint errorCapacity);

    [DllImport(Library, EntryPoint = "adame_peerless_listen_document")]
    public static extern nint ListenDocument(
        nint document,
        nint context,
        delegate* unmanaged<nint, byte*, nuint, FrameTiming*, void> onFrame,
        delegate* unmanaged<nint, int, byte*, void> onError,
        int timed,
        byte* error,
        nuint errorCapacity);

    [DllImport(Library, EntryPoint = "adame_peerless_listener_remove")]
    public static extern void Remove(nint listener, delegate* unmanaged<nint, void> release);

    [DllImport(Library, EntryPoint = "adame_peerless_compiled_firebase_version")]
    public static extern byte* CompiledFirebaseVersion();

    [DllImport(Library, EntryPoint = "adame_peerless_runtime_firestore_version")]
    public static extern byte* RuntimeFirestoreVersion();

    [DllImport(Library, EntryPoint = "adame_peerless_now_nanos")]
    public static extern ulong NowNanos();

    [DllImport(Library, EntryPoint = "adame_peerless_thread_cpu_nanos")]
    public static extern ulong ThreadCpuNanos();
}
