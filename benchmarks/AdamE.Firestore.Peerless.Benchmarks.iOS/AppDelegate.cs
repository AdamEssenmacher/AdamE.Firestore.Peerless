namespace AdamE.Firestore.Peerless.Benchmarks;

[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    public override UISceneConfiguration GetConfiguration(
        UIApplication application, UISceneSession connectingSceneSession, UISceneConnectionOptions options) =>
        new("Default Configuration", connectingSceneSession.Role);
}
