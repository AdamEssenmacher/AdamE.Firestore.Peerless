namespace AdamE.Firestore.Peerless.Tests.iOS;

/// <summary>
/// Starts the tests once the app has launched, on the thread pool, so the main queue stays free for the work they wait
/// on, then ends the process with the result.
/// </summary>
/// <remarks>
/// The app adopts the scene life cycle (Info.plist's scene manifest, <see cref="SceneDelegate"/>): built with the iOS 27
/// SDK, an app that doesn't fails to launch. The tests still start here rather than when a scene connects: on the iOS
/// 27.1 simulator this Mono build's scene connected with no configuration or delegate (the NativeAOT benchmark app's
/// does get its delegate), while <c>FinishedLaunching</c> runs on every runtime.
/// </remarks>
[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
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

    public override UISceneConfiguration GetConfiguration(
        UIApplication application, UISceneSession connectingSceneSession, UISceneConnectionOptions options) =>
        new("Default Configuration", connectingSceneSession.Role);
}
