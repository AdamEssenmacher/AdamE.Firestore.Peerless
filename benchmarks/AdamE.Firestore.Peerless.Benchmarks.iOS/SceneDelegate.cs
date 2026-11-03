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

        var options = BenchmarkOptions.FromLaunchArguments();
        void Status(string message) => UIApplication.SharedApplication.BeginInvokeOnMainThread(() => label.Text = message);
        run = Task.Run(async () =>
        {
            try
            {
                await new BenchmarkRunner(options, Status).RunAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"{BenchmarkRunner.Tag} PEERLESS_DONE crashed: {exception}");
            }
        });
    }
}
