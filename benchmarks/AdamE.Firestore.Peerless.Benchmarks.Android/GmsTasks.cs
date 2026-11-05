using Android.Gms.Tasks;
using Task = System.Threading.Tasks.Task;
using TaskCompletionSource = System.Threading.Tasks.TaskCompletionSource;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>Awaits a Play Services task without relying on any reflection-based helper.</summary>
internal static class GmsTasks
{
    // The listener's peer is disposed once the task completes, so the harness adds no GREFs that wait for the GC and
    // get counted as listener cost (the count is process-wide).
    public static async Task AsTask(this Android.Gms.Tasks.Task task)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new Listener(completion);
        _ = task.AddOnCompleteListener(listener);
        await completion.Task.ConfigureAwait(false);
    }

    private sealed class Listener(TaskCompletionSource completion) : Java.Lang.Object, IOnCompleteListener
    {
        public void OnComplete(Android.Gms.Tasks.Task task)
        {
            if (task.IsSuccessful)
                completion.TrySetResult();
            else if (task.IsCanceled)
                completion.TrySetCanceled();
            else
                completion.TrySetException(new InvalidOperationException(
                    task.Exception?.Message ?? "A Play Services task failed without an exception.", task.Exception));
        }
    }
}
