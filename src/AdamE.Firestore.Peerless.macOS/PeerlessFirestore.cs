using System.Runtime.InteropServices;

namespace AdamE.Firestore.Peerless.macOS;

/// <summary>Uses the default Firebase app in a host image that links the PeerlessMacNative Swift package product.</summary>
/// <remarks>The native image stays loaded for the process lifetime. Configure Firebase before using this client.
/// Snapshot and commit callbacks run on Firestore's configured callback queue. Dispose listeners to stop delivery.</remarks>
public sealed unsafe class PeerlessFirestore
{
    private readonly delegate* unmanaged<byte*, int, byte*, nuint, int> useEmulator;
    private readonly delegate* unmanaged<byte*, byte*, nint,
        delegate* unmanaged<nint, byte*, nuint, nint, void>,
        delegate* unmanaged<nint, int, nint, nint, void>, int, byte*, nuint, nint> listen;
    private readonly delegate* unmanaged<nint, delegate* unmanaged<nint, void>, void> remove;
    private readonly delegate* unmanaged<byte*, nint, delegate* unmanaged<nint, int, nint, void>, byte*, nuint, int> commit;

    private readonly delegate* unmanaged<nint, delegate* unmanaged<nint, void>, void> post;

    private const int ErrorCapacity = 1024;

    public PeerlessFirestore(string hostLibrary)
    {
        var image = NativeLibrary.Load(hostLibrary);
        try
        {
            nint Export(string name) => NativeLibrary.GetExport(image, "adame_peerless_mac_" + name);
            if (((delegate* unmanaged<int>)Export("abi_version"))() != 1)
                throw new InvalidOperationException("The native Peerless macOS adapter has an incompatible ABI.");

            useEmulator = (delegate* unmanaged<byte*, int, byte*, nuint, int>)Export("use_emulator");
            listen = (delegate* unmanaged<byte*, byte*, nint,
                delegate* unmanaged<nint, byte*, nuint, nint, void>,
                delegate* unmanaged<nint, int, nint, nint, void>, int, byte*, nuint, nint>)Export("listen");
            remove = (delegate* unmanaged<nint, delegate* unmanaged<nint, void>, void>)Export("remove");
            post = (delegate* unmanaged<nint, delegate* unmanaged<nint, void>, void>)Export("post");
            commit = (delegate* unmanaged<byte*, nint, delegate* unmanaged<nint, int, nint, void>, byte*, nuint, int>)Export("commit");
        }
        catch
        {
            NativeLibrary.Free(image);
            throw;
        }
    }

    /// <summary>Configure the default Firebase app's Firestore emulator before starting listeners or writes.</summary>
    public void UseEmulator(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        var hostBytes = Marshal.StringToCoTaskMemUTF8(host);
        byte* error = stackalloc byte[ErrorCapacity];
        error[0] = 0;
        try
        {
            if (useEmulator((byte*)hostBytes, port, error, ErrorCapacity) == 0)
                throw Refused(error, "configure the emulator");
        }
        finally
        {
            Marshal.FreeCoTaskMem(hostBytes);
        }
    }

    public IDisposable ListenToDocument(string documentPath, Action<FirestoreDocumentSnapshot> onSnapshot,
        Action<Exception> onError, bool includeMetadataChanges = false)
    {
        ArgumentNullException.ThrowIfNull(onSnapshot);
        return Listen(documentPath, null, SnapshotDecoder.DecodeDocument,
            snapshot => onSnapshot((FirestoreDocumentSnapshot)snapshot), onError, includeMetadataChanges);
    }

    public IDisposable ListenToQuery(string collectionPath, FirestoreQuery query, Action<FirestoreQuerySnapshot> onSnapshot,
        Action<Exception> onError, bool includeMetadataChanges = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(onSnapshot);
        return Listen(collectionPath, FirestoreRequest.Query(query), SnapshotDecoder.DecodeQuery,
            snapshot => onSnapshot((FirestoreQuerySnapshot)snapshot), onError, includeMetadataChanges);
    }

    /// <summary>Queue one atomic write batch. The completion reports the server acknowledgement or rejection.</summary>
    public void Commit(IReadOnlyList<FirestoreWrite> writes, Action<Exception?> onCompleted)
    {
        ArgumentNullException.ThrowIfNull(writes);
        ArgumentNullException.ThrowIfNull(onCompleted);
        if (writes.Count == 0)
            throw new ArgumentException("A batch needs at least one write.", nameof(writes));

        nint json = Marshal.StringToCoTaskMemUTF8(FirestoreRequest.Batch(writes));
        var context = default(GCHandle);
        byte* error = stackalloc byte[ErrorCapacity];
        error[0] = 0;
        try
        {
            context = GCHandle.Alloc(onCompleted);
            if (commit((byte*)json, GCHandle.ToIntPtr(context), &OnCommit, error, ErrorCapacity) == 0)
                throw Refused(error, "queue the batch");
        }
        catch
        {
            if (context.IsAllocated)
                context.Free();
            throw;
        }
        finally
        {
            Marshal.FreeCoTaskMem(json);
        }
    }

    private IDisposable Listen(string path, string? query, DecodeFrame decode, Action<FirestoreSnapshot> onSnapshot,
        Action<Exception> onError, bool metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(onError);
        var receiver = new Receiver(decode, onSnapshot, onError);
        nint pathBytes = Marshal.StringToCoTaskMemUTF8(path);
        nint queryBytes = 0;
        nint registration;
        var context = default(GCHandle);
        byte* error = stackalloc byte[ErrorCapacity];
        error[0] = 0;
        try
        {
            queryBytes = Marshal.StringToCoTaskMemUTF8(query);
            context = GCHandle.Alloc(receiver);
            registration = listen((byte*)pathBytes, (byte*)queryBytes, GCHandle.ToIntPtr(context),
                &OnFrame, &OnError, metadata ? 1 : 0, error, ErrorCapacity);
            if (registration == 0)
                throw Refused(error, "start listening");
        }
        catch
        {
            if (context.IsAllocated)
                context.Free();
            throw;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathBytes);
            Marshal.FreeCoTaskMem(queryBytes);
        }

        try
        {
            receiver.Gate.Attach(() => remove(registration, &Release), Post);
            return receiver;
        }
        catch
        {
            receiver.Dispose();
            throw;
        }
    }

    private void Post(Action action) => post(GCHandle.ToIntPtr(GCHandle.Alloc(action)), &DeliverPosted);

    [UnmanagedCallersOnly]
    private static void DeliverPosted(nint context)
    {
        var handle = GCHandle.FromIntPtr(context);
        var action = (Action)handle.Target!;
        handle.Free();
        action();
    }

    private static InvalidOperationException Refused(byte* message, string operation) =>
        new($"Firestore could not {operation}: {Marshal.PtrToStringUTF8((nint)message)}");

    private delegate FirestoreSnapshot DecodeFrame(ReadOnlySpan<byte> frame);

    private sealed class Receiver(DecodeFrame decode, Action<FirestoreSnapshot> onSnapshot, Action<Exception> onError)
        : IDisposable
    {
        public ListenerGate Gate { get; } = new(onError, release: static () => { });

        public void Dispose() => Gate.Stop();

        public void Deliver(nint bytes, nuint length)
        {
            FirestoreSnapshot snapshot;
            try
            {
                if (length > int.MaxValue)
                    throw new PeerlessFormatException("The shim delivered a frame over 2 GB.", 0);
                snapshot = decode(new ReadOnlySpan<byte>((void*)bytes, (int)length));
            }
            catch (Exception failure)
            {
                Gate.Fail(failure);
                return;
            }
            onSnapshot(snapshot);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnFrame(nint context, byte* bytes, nuint length, nint timing)
    {
        var receiver = (Receiver)GCHandle.FromIntPtr(context).Target!;
        receiver.Gate.Run((Receiver: receiver, Bytes: (nint)bytes, Length: length),
            static frame => frame.Receiver.Deliver(frame.Bytes, frame.Length));
    }

    [UnmanagedCallersOnly]
    private static void OnError(nint context, int code, nint message, nint native)
    {
        var receiver = (Receiver)GCHandle.FromIntPtr(context).Target!;
        var detail = Marshal.PtrToStringUTF8(message) ?? "Firestore reported an error.";
        Exception failure = code == -1
            ? new PeerlessFormatException(detail, 0)
            : new FirestoreListenException((FirestoreErrorCode)code, detail);
        receiver.Gate.Run((Receiver: receiver, Error: failure), static state => state.Receiver.Gate.Fail(state.Error));
    }

    [UnmanagedCallersOnly]
    private static void Release(nint context) => GCHandle.FromIntPtr(context).Free();

    [UnmanagedCallersOnly]
    private static void OnCommit(nint context, int code, nint message)
    {
        var handle = GCHandle.FromIntPtr(context);
        var callback = (Action<Exception?>)handle.Target!;
        handle.Free();
        callback(code == 0 ? null : new FirestoreListenException((FirestoreErrorCode)code,
            Marshal.PtrToStringUTF8(message) ?? "Firestore rejected the batch."));
    }
}
