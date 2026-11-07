namespace AdamE.Firestore.Peerless.Tests.iOS;

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
/// A tiny runner, so the host needs no test framework: each test runs on the thread pool with a timeout, and prints
/// <c>PASS name</c> or <c>FAIL name: detail</c>, then a final <c>MANAGED_TESTS passed=N failed=M</c> line, which
/// scripts/test-ios-managed.sh waits for.
/// </summary>
internal static class TestRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Runs every test in order and returns the number that failed.</summary>
    public static async Task<int> RunAsync(IReadOnlyList<TestCase> tests)
    {
        int passed = 0;
        int failed = 0;
        foreach (TestCase test in tests)
        {
            string? failure;
            try
            {
                // Task.Run, so a test that blocks synchronously can't hold up the timeout either.
                Task run = Task.Run(test.Run);
                if (await Task.WhenAny(run, Task.Delay(Timeout)).ConfigureAwait(false) != run)
                    failure = $"timed out after {Timeout.TotalSeconds:0}s";
                else
                {
                    await run.ConfigureAwait(false);
                    failure = null;
                }
            }
            catch (TestFailure exception)
            {
                failure = exception.Message;
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
            }

            if (failure is null)
            {
                passed++;
                Console.WriteLine($"PASS {test.Name}");
            }
            else
            {
                failed++;
                Console.WriteLine($"FAIL {test.Name}: {failure.ReplaceLineEndings(" | ")}");
            }
        }

        Console.WriteLine($"MANAGED_TESTS passed={passed} failed={failed}");
        return failed;
    }
}
