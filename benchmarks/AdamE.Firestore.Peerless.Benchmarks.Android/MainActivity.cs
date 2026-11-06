using Android.Util;

namespace AdamE.Firestore.Peerless.Benchmarks;

[Activity(Label = "Peerless Bench", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private static Task? run;

    // The run outlives any one Activity (a rotation recreates it), so status goes to whichever view is current and the
    // runner holds the application context, not an Activity.
    private static TextView? statusView;
    private static volatile string lastStatus = "Starting…";

    private TextView? ownView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var text = new TextView(this) { Text = lastStatus };
        text.SetPadding(48, 96, 48, 48);
        SetContentView(text);
        statusView = ownView = text;

        if (run is not null)
            return;

        Android.Content.Intent? intent = Intent;
        Android.Content.Context context = ApplicationContext ?? this;
        run = Task.Run(async () =>
        {
            try
            {
                var options = BenchmarkOptions.From(intent);
                await new BenchmarkRunner(context, options, Status).RunAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Log.Error(BenchmarkRunner.Tag, $"PEERLESS_DONE crashed: {exception}");
            }
        });
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(statusView, ownView))
            statusView = null;
        base.OnDestroy();
    }

    // One Handler and one Runnable for the whole run: a new pair per status line would add GREFs that wait for the GC,
    // and the GREF count the benchmark records is process-wide.
    private static readonly Android.OS.Handler MainThread = new(Android.OS.Looper.MainLooper!);
    private static readonly ShowStatus ShowLastStatus = new();

    private static void Status(string message)
    {
        lastStatus = message;
        _ = MainThread.Post(ShowLastStatus);
    }

    private sealed class ShowStatus : Java.Lang.Object, Java.Lang.IRunnable
    {
        public void Run()
        {
            if (statusView is { } view)
                view.Text = lastStatus;
        }
    }
}
