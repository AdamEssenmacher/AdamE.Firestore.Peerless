namespace AdamE.Firestore.Peerless.Tests.Android;

/// <summary>
/// Starts the tests on the thread pool, so the main looper stays free for the work they wait on, then finishes.
/// scripts/test-android-managed.sh launches it with <c>--es run &lt;token&gt;</c>.
/// </summary>
[Activity(Label = "Peerless Tests", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    // One run per process: a recreated Activity (a rotation) must not start the tests again.
    private static Task? run;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(new TextView(this) { Text = "Running managed tests…" });
        if (run is not null)
            return;

        string token = Intent?.GetStringExtra("run") ?? "none";
        run = Task.Run(async () =>
        {
            try
            {
                await TestRunner.RunAsync(ReceiverTests.All, token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // The runner catches each test's failure; this is the runner itself breaking. Still log the line the
                // script waits for, so it fails at once instead of timing out.
                TestRunner.Write($"FAIL runner: {exception.ToString().ReplaceLineEndings(" | ")}");
                TestRunner.Write("MANAGED_TESTS passed=0 failed=1");
            }

            RunOnUiThread(Finish);
        });
    }
}
