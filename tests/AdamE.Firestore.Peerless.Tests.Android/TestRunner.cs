using Android.Util;

namespace AdamE.Firestore.Peerless.Tests.Android;

/// <summary>One named test. Synchronous tests return a completed task.</summary>
internal sealed record TestCase(string Name, Func<Task> Run);

/// <summary>Thrown by <see cref="Check.That"/>; its message is the failure detail.</summary>
internal sealed class TestFailure(string message) : Exception(message);

internal static class Check
{
    public static void That(bool condition, string detail)
    {
        if (!condition)
            throw new TestFailure(detail);
    }
}

/// <summary>
/// A tiny runner, so the host needs no test framework: each test runs on the thread pool with a timeout, and logs
/// <c>PASS name</c> or <c>FAIL name: detail</c>, then a final <c>MANAGED_TESTS passed=N failed=M</c> line, to logcat
/// under <see cref="Tag"/>, which scripts/test-android-managed.sh waits for.
/// </summary>
internal static class TestRunner
{
    /// <summary>The logcat tag every runner line goes to.</summary>
    public const string Tag = "PeerlessTests";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // logd truncates an entry near 4 KB; a stack trace is cut well before that so the line stays whole.
    private const int MaxLine = 3000;

    /// <summary>
    /// Runs every test in order and returns the number that failed. <paramref name="run"/> (the script's token) is
    /// logged first, so the script reads only this run's lines without clearing anyone's logcat.
    /// </summary>
    public static async Task<int> RunAsync(IReadOnlyList<TestCase> tests, string run)
    {
        Write($"MANAGED_TESTS_BEGIN {run}");
        int passed = 0;
        int failed = 0;
        bool timedOut = false;
        foreach (TestCase test in tests)
        {
            string? failure;
            // A timed-out test keeps running and may hold the main looper or shared state, so later results would
            // mislead; they are reported as failures without running.
            if (timedOut)
                failure = "skipped: an earlier test timed out";
            else
                (failure, timedOut) = await RunOneAsync(test).ConfigureAwait(false);

            if (failure is null)
            {
                passed++;
                Write($"PASS {test.Name}");
            }
            else
            {
                failed++;
                Write($"FAIL {test.Name}: {failure.ReplaceLineEndings(" | ")}");
            }
        }

        Write($"MANAGED_TESTS passed={passed} failed={failed}");
        return failed;
    }

    // The failure detail (null on a pass), and whether the test timed out.
    private static async Task<(string? Failure, bool TimedOut)> RunOneAsync(TestCase test)
    {
        try
        {
            // Task.Run, so a test that blocks synchronously can't hold up the timeout either.
            Task task = Task.Run(test.Run);
            if (await Task.WhenAny(task, Task.Delay(Timeout)).ConfigureAwait(false) != task)
                return ($"timed out after {Timeout.TotalSeconds:0}s", true);
            await task.ConfigureAwait(false);
            return (null, false);
        }
        catch (TestFailure exception)
        {
            return (exception.Message, false);
        }
        catch (Exception exception)
        {
            return (exception.ToString(), false);
        }
    }

    public static void Write(string line) =>
        Log.Info(Tag, line.Length <= MaxLine ? line : string.Concat(line.AsSpan(0, MaxLine), "…"));
}
