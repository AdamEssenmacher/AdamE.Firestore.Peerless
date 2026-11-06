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
                () => Released++);
            Gate = gate;
        }

        public ListenerGate Gate { get; }

        public List<Exception> Errors { get; } = [];

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
