namespace AdamE.Firestore.Peerless.Tests.iOS;

/// <summary>
/// The scene delegate the scene manifest names, so the app adopts the scene life cycle. It only shows a blank window:
/// the tests start in <see cref="AppDelegate.FinishedLaunching"/>, which runs whether or not a scene connects here.
/// </summary>
[Register("SceneDelegate")]
public sealed class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
            return;

        var window = new UIWindow(windowScene) { RootViewController = new UIViewController() };
        Window = window;
        window.MakeKeyAndVisible();
    }
}
