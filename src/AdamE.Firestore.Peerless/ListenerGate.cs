namespace AdamE.Firestore.Peerless;

/// <summary>
/// The concurrency contract every platform's listener shares, kept free of platform code so it can be tested
/// directly. One gate per listener:
/// <list type="bullet">
/// <item>Each callback runs through <see cref="Run(Action)"/>, under the gate's lock, and is skipped once the listener is
/// closed. So <see cref="Stop"/> from another thread waits for a callback in flight, and none starts after it
/// returns.</item>
/// <item><see cref="Stop"/> from inside a callback (same thread) re-enters the lock and returns at once; that callback
/// is the last.</item>
/// <item>The first failure closes the listener, removes the native registration and reports the error, once. A failure
/// before <see cref="Attach"/> (the native listener fired before <c>Listen</c> returned) is held and delivered after
/// attaching, through the platform's callback scheduler, so the registration can be removed first. When <c>Listen</c>
/// was called off the callback thread, that delivery can still run before the caller has stored the returned
/// handle.</item>
/// <item>The release action (for example, freeing a native buffer) runs exactly once, after closing, and never while
/// a callback is running.</item>
/// </list>
/// </summary>
internal sealed class ListenerGate
{
    private readonly object sync = new();
    private readonly Action<Exception> onError;
    private readonly Action<Action> postToCallbackThread;
    private readonly Action release;

    private Action? removeRegistration;
    private Exception? pendingError;
    private bool attached;
    private bool closed;
    private bool stopped;
    private bool inCallback;
    private bool released;
    private int removed;

    /// <param name="onError">The app's error callback.</param>
    /// <param name="postToCallbackThread">Runs an action later on the thread callbacks normally arrive on.</param>
    /// <param name="release">Frees the listener's resources; called exactly once.</param>
    public ListenerGate(Action<Exception> onError, Action<Action> postToCallbackThread, Action release)
    {
        this.onError = onError;
        this.postToCallbackThread = postToCallbackThread;
        this.release = release;
    }

    /// <summary>Whether the listener has stopped, for whatever reason.</summary>
    public bool IsClosed
    {
        get
        {
            lock (sync)
                return closed;
        }
    }

    /// <summary>Records how to remove the native registration, once <c>Listen</c> has it.</summary>
    public void Attach(Action remove)
    {
        Exception? deliver;
        bool removeNow;
        lock (sync)
        {
            removeRegistration = remove;
            attached = true;
            removeNow = closed;
            deliver = pendingError;
            pendingError = null;
        }

        if (removeNow)
            RemoveOnce();
        if (deliver is not null)
            postToCallbackThread(() => RunCore(
                () =>
                {
                    if (!stopped) // checked when it runs: Stop() after Attach still cancels it
                        onError(deliver);
                },
                evenIfClosed: true));
    }

    /// <summary>Runs one callback unless the listener is closed. Returns whether it ran.</summary>
    public bool Run(Action callback) => RunCore(callback, evenIfClosed: false);

    /// <summary>
    /// Inside <see cref="Run(Action)"/> only: closes the listener because of <paramref name="error"/>, removes the
    /// registration and reports the error (now, or after <see cref="Attach"/> if that hasn't happened yet).
    /// </summary>
    public void Fail(Exception error)
    {
        if (!Monitor.IsEntered(sync))
            throw new InvalidOperationException("Fail is called from inside Run.");
        if (closed)
            return;

        closed = true;
        if (!attached)
        {
            pendingError = error;
            return;
        }

        RemoveOnce();
        onError(error);
    }

    /// <summary>
    /// Stops the listener: waits for a callback running on another thread, and makes sure none starts afterwards. From
    /// inside a callback it returns at once.
    /// </summary>
    public void Stop()
    {
        bool remove;
        lock (sync)
        {
            stopped = true;
            pendingError = null; // an error not yet delivered never will be: the app asked to stop
            remove = attached;
            closed = true;
            if (!inCallback)
                ReleaseOnce();
        }

        if (remove)
            RemoveOnce();
    }

    private bool RunCore(Action callback, bool evenIfClosed)
    {
        lock (sync)
        {
            if (closed && !evenIfClosed)
                return false;

            bool outer = !inCallback;
            inCallback = true;
            try
            {
                callback();
            }
            finally
            {
                if (outer)
                {
                    inCallback = false;
                    if (closed)
                        ReleaseOnce();
                }
            }

            return true;
        }
    }

    private void ReleaseOnce()
    {
        if (released)
            return;
        released = true;
        release();
    }

    private void RemoveOnce()
    {
        Action? remove;
        lock (sync)
            remove = removeRegistration;
        if (remove is not null && Interlocked.Exchange(ref removed, 1) == 0)
            remove();
    }
}
