namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>Shows the run's progress and starts it once, on the thread pool, so listener callbacks own the main queue.</summary>
[Register("SceneDelegate")]
public sealed class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    private static Task? run;

    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
            return;

        var label = new UILabel
        {
            Text = "Starting…",
            Lines = 0,
            TextAlignment = UITextAlignment.Center,
            BackgroundColor = UIColor.SystemBackground,
        };
        var window = new UIWindow(windowScene) { RootViewController = new UIViewController { View = label } };
        Window = window;
        window.MakeKeyAndVisible();

        if (run is not null)
            return;

        BenchmarkOptions options;
        try
        {
            options = BenchmarkOptions.FromLaunchArguments();
        }
        catch (Exception exception)
        {
            // Bad launch arguments: record why, where the script looks, instead of leaving it to see a dead process.
            BenchmarkRunner.RecordStartupFailure(exception);
            label.Text = $"Bad launch arguments: {exception.Message}";
            run = Task.CompletedTask;
            return;
        }

        // A GCD block, not BeginInvokeOnMainThread: that makes an NSObject dispatcher per call, which would sit in the
        // peer table until the next settle and be counted as listener cost.
        void Status(string message) => CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() => label.Text = message);
        run = Task.Run(async () =>
        {
            try
            {
                await new BenchmarkRunner(options, Status).RunAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Only reached when the result files couldn't even be opened. End the process, so the script reports
                // process-died at once instead of waiting out its timeout.
                Console.WriteLine($"{BenchmarkRunner.Tag} PEERLESS_DONE crashed: {exception}");
                Environment.FailFast($"The benchmark could not start: {exception.Message}", exception);
            }
        });
    }
}
