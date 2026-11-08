using Xunit;

namespace AdamE.Firestore.Peerless.Tests;

/// <summary>The listener concurrency contract the Android (and iOS) listeners rely on, tested without a device.</summary>
public sealed class ListenerGateTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>Which <see cref="ListenerGate"/> Run overload a test drives; both keep the same contract.</summary>
    public enum RunOverload
    {
        Action,
        State,
    }

    public static TheoryData<RunOverload> Overloads => [RunOverload.Action, RunOverload.State];

    [Theory]
    [MemberData(nameof(Overloads))]
    public void A_failure_before_attach_is_held_then_delivered_once_after_attach(RunOverload overload)
    {
        var harness = new Harness(overload);
        var error = new InvalidOperationException("early");

        Assert.True(harness.Run(() => harness.Gate.Fail(error)));

        Assert.Empty(harness.Errors);
        Assert.Empty(harness.Posted);
        Assert.Equal(1, harness.Released); // closed, and no callback running

        harness.Gate.Attach(harness.Remove, harness.Posted.Add);

        Assert.Equal(1, harness.Removed);
        Assert.Empty(harness.Errors); // not inline: Listen hasn't returned the app's handle yet
        harness.RunPosted();
        Assert.Same(error, Assert.Single(harness.Errors));
        Assert.Equal(1, harness.Released);
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void Stopping_after_attach_cancels_an_early_failure_not_yet_delivered(RunOverload overload)
    {
        var harness = new Harness(overload);
        harness.Run(() => harness.Gate.Fail(new InvalidOperationException("early")));
        harness.Gate.Attach(harness.Remove, harness.Posted.Add);

        harness.Gate.Stop();
        harness.RunPosted();

        Assert.Empty(harness.Errors);
        Assert.Equal(1, harness.Removed);
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void Stopping_inside_a_snapshot_callback_returns_at_once_and_releases_after_the_callback(RunOverload overload)
    {
        var harness = new Harness(overload);
        harness.Gate.Attach(harness.Remove, harness.Posted.Add);
        int releasedDuringCallback = -1;

        Assert.True(harness.Run(() =>
        {
            harness.Gate.Stop();
            releasedDuringCallback = harness.Released;
        }));

        Assert.Equal(0, releasedDuringCallback);
        Assert.Equal(1, harness.Released);
        Assert.Equal(1, harness.Removed);
        Assert.False(harness.Run(() => Assert.Fail("A callback ran after Stop.")));
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void Stopping_inside_the_error_callback_does_not_deadlock_or_report_twice(RunOverload overload)
    {
        var harness = new Harness(overload, onError: (gate, _) => gate.Stop());
        harness.Gate.Attach(harness.Remove, harness.Posted.Add);

        harness.Run(() => harness.Gate.Fail(new InvalidOperationException("boom")));
        harness.Run(() => harness.Gate.Fail(new InvalidOperationException("again")));

        Assert.Single(harness.Errors);
        Assert.Equal(1, harness.Removed);
        Assert.Equal(1, harness.Released);
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task Stopping_from_another_thread_waits_for_the_callback_in_flight(RunOverload overload)
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        var harness = new Harness(overload);
        harness.Gate.Attach(harness.Remove, harness.Posted.Add);
        using var started = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        int releasedWhileRunning = -1;

        Task callback = Task.Run(() => harness.Run(() =>
        {
            started.Set();
            proceed.Wait(Wait, cancel);
            releasedWhileRunning = harness.Released;
        }), cancel);
        Assert.True(started.Wait(Wait, cancel));

        Task stop = Task.Run(harness.Gate.Stop, cancel);
        Task first = await Task.WhenAny(stop, Task.Delay(200, cancel));
        Assert.NotSame(stop, first); // Stop must still be waiting for the running callback

        proceed.Set();
        await stop.WaitAsync(Wait, cancel);
        await callback.WaitAsync(Wait, cancel);
        Assert.Equal(0, releasedWhileRunning);
        Assert.Equal(1, harness.Released);
        Assert.False(harness.Run(() => Assert.Fail("A callback ran after Stop.")));
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void A_frame_racing_removal_is_dropped(RunOverload overload)
    {
        var harness = new Harness(overload);
        harness.Gate.Attach(harness.Remove, harness.Posted.Add);
        harness.Gate.Stop();

        bool ran = harness.Run(() => Assert.Fail("A frame was delivered after Stop."));

        Assert.False(ran);
        Assert.True(harness.Gate.IsClosed);
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void Resources_are_released_exactly_once_whatever_happens(RunOverload overload)
    {
        var harness = new Harness(overload);
        harness.Gate.Attach(harness.Remove, harness.Posted.Add);

        harness.Run(() => harness.Gate.Fail(new InvalidOperationException("one")));
        harness.Gate.Stop();
        harness.Gate.Stop();
        harness.Run(() => harness.Gate.Fail(new InvalidOperationException("two")));

        Assert.Equal(1, harness.Released);
        Assert.Equal(1, harness.Removed);
        Assert.Single(harness.Errors);
    }

    [Fact]
    public void Stop_before_attach_removes_the_registration_when_it_arrives()
    {
        var harness = new Harness(RunOverload.Action);
        harness.Gate.Stop();

        harness.Gate.Attach(harness.Remove, harness.Posted.Add);

        Assert.Equal(1, harness.Removed);
        Assert.Equal(1, harness.Released);
    }

    // iOS FrameReceiver.Start and Android PeerlessListener.Register call Stop in a catch around Attach. That relies on
    // Attach recording the remover before anything in it can throw, so the remover runs once in total.
    [Theory]
    [MemberData(nameof(Overloads))]
    public void A_throwing_post_in_attach_propagates_and_stop_removes_once_and_never_delivers(RunOverload overload)
    {
        var harness = new Harness(overload);
        harness.Run(() => harness.Gate.Fail(new InvalidOperationException("early")));
        var refused = new InvalidOperationException("post refused");
        int posts = 0;

        Exception thrown = Assert.Throws<InvalidOperationException>(() => AttachLikeThePlatforms(
            harness.Gate,
            harness.Remove,
            _ =>
            {
                posts++;
                throw refused;
            }));

        Assert.Same(refused, thrown);
        Assert.Equal(1, posts);
        Assert.Equal(1, harness.Removed); // Attach's own removal and Stop's, together
        Assert.Equal(1, harness.Released);
        Assert.Empty(harness.Errors);
        Assert.True(harness.Gate.IsClosed);
        Assert.False(harness.Run(() => Assert.Fail("A callback ran after Stop.")));
    }

    [Fact]
    public void A_post_that_throws_after_queueing_still_never_delivers_once_stopped()
    {
        var harness = new Harness(RunOverload.Action);
        harness.Run(() => harness.Gate.Fail(new InvalidOperationException("early")));

        Assert.Throws<InvalidOperationException>(() => AttachLikeThePlatforms(
            harness.Gate,
            harness.Remove,
            action =>
            {
                harness.Posted.Add(action); // a scheduler that queued the work, then reported failure
                throw new InvalidOperationException("post failed late");
            }));
        harness.RunPosted();

        Assert.Empty(harness.Errors);
        Assert.Equal(1, harness.Removed);
        Assert.Equal(1, harness.Released);
    }

    [Fact]
    public void A_remover_throwing_inside_attach_is_not_called_again_by_stop()
    {
        var harness = new Harness(RunOverload.Action);
        harness.Run(() => harness.Gate.Fail(new InvalidOperationException("early")));
        var removeFailed = new InvalidOperationException("remove failed");
        int removes = 0;

        Exception thrown = Assert.Throws<InvalidOperationException>(() => AttachLikeThePlatforms(
            harness.Gate,
            () =>
            {
                removes++;
                throw removeFailed;
            },
            harness.Posted.Add));

        Assert.Same(removeFailed, thrown);
        Assert.Equal(1, removes);
        Assert.Empty(harness.Posted); // the remover threw before the held error was posted
        Assert.Empty(harness.Errors);
        Assert.Equal(1, harness.Released);
    }

    // #52: a held error Stop cancels goes to onDropped, so the platform can free what it holds (a Java cause's global
    // reference on Android). Each path: delivered or dropped, exactly once.
    [Theory]
    [MemberData(nameof(Overloads))]
    public void Stop_before_attach_drops_a_held_failure_once(RunOverload overload)
    {
        var harness = new Harness(overload);
        var error = new InvalidOperationException("early");
        harness.Run(() => harness.Gate.Fail(error));

        harness.Gate.Stop();
        harness.Gate.Stop();
        harness.Gate.Attach(harness.Remove, harness.Posted.Add);
        harness.RunPosted();

        Assert.Same(error, Assert.Single(harness.Dropped));
        Assert.Empty(harness.Errors);
        Assert.Empty(harness.Posted);
        Assert.Equal(1, harness.Removed);
        Assert.Equal(1, harness.Released);
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void Stop_after_attach_before_the_posted_delivery_drops_the_failure_once(RunOverload overload)
    {
        var harness = new Harness(overload);
        var error = new InvalidOperationException("early");
        harness.Run(() => harness.Gate.Fail(error));
        harness.Gate.Attach(harness.Remove, harness.Posted.Add);

        harness.Gate.Stop();
        Assert.Same(error, Assert.Single(harness.Dropped)); // on Stop's thread, not left to the posted action
        harness.RunPosted();
        harness.Gate.Stop();

        Assert.Single(harness.Dropped);
        Assert.Empty(harness.Errors);
        Assert.Equal(1, harness.Removed);
        Assert.Equal(1, harness.Released);
    }

    // Attach throwing on each side of queueing the delivery, or in the remover before it, as Register and Start see.
    public static TheoryData<string> AttachFailures => ["post refused", "post failed late", "remove failed"];

    [Theory]
    [MemberData(nameof(AttachFailures))]
    public void A_held_failure_is_dropped_once_when_attach_throws_and_stop_follows(string failure)
    {
        var harness = new Harness(RunOverload.Action);
        var error = new InvalidOperationException("early");
        harness.Run(() => harness.Gate.Fail(error));
        int removes = 0;

        Assert.Throws<InvalidOperationException>(() => AttachLikeThePlatforms(
            harness.Gate,
            () =>
            {
                removes++;
                if (failure == "remove failed")
                    throw new InvalidOperationException(failure);
            },
            action =>
            {
                if (failure == "post failed late")
                    harness.Posted.Add(action);
                throw new InvalidOperationException(failure);
            }));
        harness.RunPosted();

        Assert.Same(error, Assert.Single(harness.Dropped));
        Assert.Empty(harness.Errors);
        Assert.Equal(1, removes);
        Assert.Equal(1, harness.Released);
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void A_delivered_early_failure_is_never_dropped(RunOverload overload)
    {
        var harness = new Harness(overload);
        var error = new InvalidOperationException("early");
        harness.Run(() => harness.Gate.Fail(error));
        harness.Gate.Attach(harness.Remove, harness.Posted.Add);

        harness.RunPosted();
        harness.Gate.Stop();

        Assert.Same(error, Assert.Single(harness.Errors));
        Assert.Empty(harness.Dropped);
        Assert.Equal(1, harness.Removed);
        Assert.Equal(1, harness.Released);
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void Stopping_inside_the_delivery_of_an_early_failure_does_not_drop_it(RunOverload overload)
    {
        var harness = new Harness(overload, onError: (gate, _) => gate.Stop());
        harness.Run(() => harness.Gate.Fail(new InvalidOperationException("early")));
        harness.Gate.Attach(harness.Remove, harness.Posted.Add);

        harness.RunPosted();

        Assert.Single(harness.Errors);
        Assert.Empty(harness.Dropped);
        Assert.Equal(1, harness.Removed);
        Assert.Equal(1, harness.Released);
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public void Nothing_is_dropped_when_no_failure_was_held(RunOverload overload)
    {
        var stoppedEarly = new Harness(overload);
        stoppedEarly.Gate.Stop();
        stoppedEarly.Gate.Attach(stoppedEarly.Remove, stoppedEarly.Posted.Add);

        var failedAttached = new Harness(overload);
        failedAttached.Gate.Attach(failedAttached.Remove, failedAttached.Posted.Add);
        failedAttached.Run(() => failedAttached.Gate.Fail(new InvalidOperationException("late")));
        failedAttached.Gate.Stop();

        Assert.Empty(stoppedEarly.Dropped);
        Assert.Empty(failedAttached.Dropped);
        Assert.Single(failedAttached.Errors);
        Assert.Equal(1, stoppedEarly.Removed);
        Assert.Equal(1, failedAttached.Removed);
        Assert.Equal(1, stoppedEarly.Released);
        Assert.Equal(1, failedAttached.Released);
    }

    [Fact]
    public void The_drop_action_runs_outside_the_gates_lock()
    {
        bool lockFree = false;
        ListenerGate? gate = null;
        gate = new ListenerGate(
            _ => Assert.Fail("delivered"),
            static () => { },
            _ =>
            {
                // Another thread can take the gate only if Stop has left its lock.
                lockFree = Task.Run(() => gate!.IsClosed).Wait(Wait, TestContext.Current.CancellationToken);
            });
        gate.Run(() => gate.Fail(new InvalidOperationException("early")));
        gate.Attach(static () => { }, static _ => { });

        gate.Stop();

        Assert.True(lockFree);
    }

    [Fact]
    public void Fail_outside_a_callback_is_a_programming_error()
    {
        var harness = new Harness(RunOverload.Action);

        Assert.Throws<InvalidOperationException>(() => harness.Gate.Fail(new InvalidOperationException()));
    }

    [Fact]
    public void The_state_overload_passes_its_state_to_the_callback()
    {
        var harness = new Harness(RunOverload.State);
        var seen = new List<(int, string)>();

        Assert.True(harness.Gate.Run((seen, number: 7, text: "seven"), static state => state.seen.Add((state.number, state.text))));

        Assert.Equal((7, "seven"), Assert.Single(seen));
    }

    // The frame path: a static lambda and a value-type state, as the platforms' FrameReceiver use it. After warm-up a
    // call allocates nothing; the capturing Action form, for comparison, allocates a closure and a delegate per call.
    [Fact]
    public void The_state_overload_with_a_static_lambda_allocates_nothing_per_call()
    {
        var harness = new Harness(RunOverload.State);
        harness.Gate.Attach(harness.Remove, harness.Posted.Add);
        var sink = new Counter();
        const int Calls = 10_000;

        long stateBytes = Measure(() =>
        {
            for (int i = 0; i < Calls; i++)
                harness.Gate.Run((sink, i), static state => state.sink.Add(state.i));
        });
        long actionBytes = Measure(() =>
        {
            for (int i = 0; i < Calls; i++)
            {
                int captured = i;
                harness.Gate.Run(() => sink.Add(captured));
            }
        });

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Bytes per call: state overload {(double)stateBytes / Calls}, capturing Action {(double)actionBytes / Calls}.");
        Assert.Equal(0, stateBytes);
        Assert.True(actionBytes >= Calls * 32, $"Expected the capturing lambda to allocate; it allocated {actionBytes} bytes.");
        Assert.Equal(4L * Calls * ((long)Calls - 1) / 2, sink.Total); // every call ran, both ways, warm-up included
    }

    // The platforms' pattern: Attach, and on any exception Stop (which must remove exactly once) and rethrow.
    private static void AttachLikeThePlatforms(ListenerGate gate, Action remove, Action<Action> postToCallbackThread)
    {
        try
        {
            gate.Attach(remove, postToCallbackThread);
        }
        catch
        {
            gate.Stop();
            throw;
        }
    }

    // Runs once to warm up (JIT, lock inflation, the static lambda's cached delegate), then measures a second run.
    private static long Measure(Action run)
    {
        run();
        long before = GC.GetAllocatedBytesForCurrentThread();
        run();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class Counter
    {
        public long Total { get; private set; }

        public void Add(int value) => Total += value;
    }

    private sealed class Harness
    {
        private readonly RunOverload overload;

        public Harness(RunOverload overload, Action<ListenerGate, Exception>? onError = null)
        {
            this.overload = overload;
            ListenerGate? gate = null;
            gate = new ListenerGate(
                error =>
                {
                    Errors.Add(error);
                    if (gate is not null)
                        onError?.Invoke(gate, error);
                },
                () => Released++,
                Dropped.Add);
            Gate = gate;
        }

        public ListenerGate Gate { get; }

        public List<Exception> Errors { get; } = [];

        public List<Exception> Dropped { get; } = [];

        public List<Action> Posted { get; } = [];

        public int Released { get; private set; }

        public int Removed { get; private set; }

        public void Remove() => Removed++;

        // The state overload gets the callback as its state, through a static lambda, as the frame receivers use it.
        public bool Run(Action callback) => overload == RunOverload.State
            ? Gate.Run(callback, static callback => callback())
            : Gate.Run(callback);

        public void RunPosted()
        {
            foreach (Action action in Posted.ToArray())
                action();
            Posted.Clear();
        }
    }
}
