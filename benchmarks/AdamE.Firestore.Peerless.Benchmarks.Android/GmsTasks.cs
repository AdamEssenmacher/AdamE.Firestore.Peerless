using Android.Gms.Tasks;
using Task = System.Threading.Tasks.Task;
using TaskCompletionSource = System.Threading.Tasks.TaskCompletionSource;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>Awaits a Play Services task without relying on any reflection-based helper.</summary>
internal static class GmsTasks
{
    public static Task AsTask(this Android.Gms.Tasks.Task task)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = task.AddOnCompleteListener(new Listener(completion));
        return completion.Task;
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
                    task.Exception?.Message ?? "A Play Services task failed without an exception."));
        }
    }
}
