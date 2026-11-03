using Xunit;

namespace AdamE.Firestore.Peerless.Tests;

/// <summary>The listener concurrency contract the Android (and iOS) listeners rely on, tested without a device.</summary>
public sealed class ListenerGateTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public void A_failure_before_attach_is_held_then_delivered_once_after_attach()
    {
        var harness = new Harness();
        var error = new InvalidOperationException("early");

        Assert.True(harness.Gate.Run(() => harness.Gate.Fail(error)));

        Assert.Empty(harness.Errors);
        Assert.Empty(harness.Posted);
        Assert.Equal(1, harness.Released); // closed, and no callback running

        harness.Gate.Attach(harness.Remove);

        Assert.Equal(1, harness.Removed);
        Assert.Empty(harness.Errors); // not inline: Listen hasn't returned the app's handle yet
        harness.RunPosted();
        Assert.Same(error, Assert.Single(harness.Errors));
        Assert.Equal(1, harness.Released);
    }

    [Fact]
    public void Stopping_after_attach_cancels_an_early_failure_not_yet_delivered()
    {
        var harness = new Harness();
        harness.Gate.Run(() => harness.Gate.Fail(new InvalidOperationException("early")));
        harness.Gate.Attach(harness.Remove);

        harness.Gate.Stop();
        harness.RunPosted();

        Assert.Empty(harness.Errors);
        Assert.Equal(1, harness.Removed);
    }

    [Fact]
    public void Stopping_inside_a_snapshot_callback_returns_at_once_and_releases_after_the_callback()
    {
        var harness = new Harness();
        harness.Gate.Attach(harness.Remove);
        int releasedDuringCallback = -1;

        Assert.True(harness.Gate.Run(() =>
        {
            harness.Gate.Stop();
            releasedDuringCallback = harness.Released;
        }));

        Assert.Equal(0, releasedDuringCallback);
        Assert.Equal(1, harness.Released);
        Assert.Equal(1, harness.Removed);
        Assert.False(harness.Gate.Run(() => Assert.Fail("A callback ran after Stop.")));
    }

    [Fact]
    public void Stopping_inside_the_error_callback_does_not_deadlock_or_report_twice()
    {
        var harness = new Harness(onError: (gate, _) => gate.Stop());
        harness.Gate.Attach(harness.Remove);

        harness.Gate.Run(() => harness.Gate.Fail(new InvalidOperationException("boom")));
        harness.Gate.Run(() => harness.Gate.Fail(new InvalidOperationException("again")));

        Assert.Single(harness.Errors);
        Assert.Equal(1, harness.Removed);
        Assert.Equal(1, harness.Released);
    }

    [Fact]
    public async Task Stopping_from_another_thread_waits_for_the_callback_in_flight()
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        var harness = new Harness();
        harness.Gate.Attach(harness.Remove);
        using var started = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        int releasedWhileRunning = -1;

        Task callback = Task.Run(() => harness.Gate.Run(() =>
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
        Assert.False(harness.Gate.Run(() => Assert.Fail("A callback ran after Stop.")));
    }

    [Fact]
    public void A_frame_racing_removal_is_dropped()
    {
        var harness = new Harness();
        harness.Gate.Attach(harness.Remove);
        harness.Gate.Stop();

        bool ran = harness.Gate.Run(() => Assert.Fail("A frame was delivered after Stop."));

        Assert.False(ran);
        Assert.True(harness.Gate.IsClosed);
    }

    [Fact]
    public void Resources_are_released_exactly_once_whatever_happens()
    {
        var harness = new Harness();
        harness.Gate.Attach(harness.Remove);

        harness.Gate.Run(() => harness.Gate.Fail(new InvalidOperationException("one")));
        harness.Gate.Stop();
        harness.Gate.Stop();
        harness.Gate.Run(() => harness.Gate.Fail(new InvalidOperationException("two")));

        Assert.Equal(1, harness.Released);
        Assert.Equal(1, harness.Removed);
        Assert.Single(harness.Errors);
    }

    [Fact]
    public void Stop_before_attach_removes_the_registration_when_it_arrives()
    {
        var harness = new Harness();
        harness.Gate.Stop();

        harness.Gate.Attach(harness.Remove);

        Assert.Equal(1, harness.Removed);
        Assert.Equal(1, harness.Released);
    }

    [Fact]
    public void Fail_outside_a_callback_is_a_programming_error()
    {
        var harness = new Harness();

        Assert.Throws<InvalidOperationException>(() => harness.Gate.Fail(new InvalidOperationException()));
    }

    private sealed class Harness
    {
        public Harness(Action<ListenerGate, Exception>? onError = null)
        {
            ListenerGate? gate = null;
            gate = new ListenerGate(
                error =>
                {
                    Errors.Add(error);
                    if (gate is not null)
                        onError?.Invoke(gate, error);
                },
                Posted.Add,
                () => Released++);
            Gate = gate;
        }

        public ListenerGate Gate { get; }

        public List<Exception> Errors { get; } = [];

        public List<Action> Posted { get; } = [];

        public int Released { get; private set; }

        public int Removed { get; private set; }

        public void Remove() => Removed++;

        public void RunPosted()
        {
            foreach (Action action in Posted.ToArray())
                action();
            Posted.Clear();
        }
    }
}
