using Android.Util;

namespace AdamE.Firestore.Peerless.Benchmarks;

[Activity(Label = "Peerless Bench", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private static Task? run;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var text = new TextView(this) { Text = "Starting…" };
        text.SetPadding(48, 96, 48, 48);
        SetContentView(text);

        if (run is not null)
            return;

        var options = BenchmarkOptions.From(Intent);
        void Status(string message) => RunOnUiThread(() => text.Text = message);
        run = Task.Run(async () =>
        {
            try
            {
                await new BenchmarkRunner(this, options, Status).RunAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Log.Error(BenchmarkRunner.Tag, $"PEERLESS_DONE crashed: {exception}");
            }
        });
    }
}
