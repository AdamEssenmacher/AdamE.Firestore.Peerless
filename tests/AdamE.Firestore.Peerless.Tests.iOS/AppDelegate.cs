namespace AdamE.Firestore.Peerless.Tests.iOS;

/// <summary>
/// Starts the tests once the app has launched, on the thread pool, so the main queue stays free for the work they wait
/// on, then ends the process with the result.
/// </summary>
[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    // No window: nothing is shown, and the tests need only the main queue, which UIApplication.Main runs.
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        _ = Task.Run(async () =>
        {
            int failed;
            try
            {
                failed = await TestRunner.RunAsync(ReceiverTests.All).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // The runner catches each test's failure; this is the runner itself breaking. Still print the line
                // the script waits for, so it fails at once instead of timing out.
                Console.WriteLine($"FAIL runner: {exception}");
                Console.WriteLine("MANAGED_TESTS passed=0 failed=1");
                failed = 1;
            }

            Console.Out.Flush();
            // A non-zero exit code too, for anything that reads it rather than the summary line.
            Environment.Exit(failed == 0 ? 0 : 1);
        });
        return true;
    }
}
